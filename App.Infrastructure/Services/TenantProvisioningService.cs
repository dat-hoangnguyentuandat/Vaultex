using App.Domain.Constants;
using App.Domain.Entities;
using App.Infrastructure.Data;
using App.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace App.Infrastructure.Services
{
    public class TenantProvisioningService
    {
        private readonly PlatformDbContext _platformDb;
        private readonly TenantConnectionStringFactory _connectionFactory;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOptions<TenantConnectionOptions> _options;
        private readonly ILogger<TenantProvisioningService> _logger;
        private readonly PlatformUserDirectoryService _directory;

        public TenantProvisioningService(
            PlatformDbContext platformDb,
            TenantConnectionStringFactory connectionFactory,
            IServiceScopeFactory scopeFactory,
            IOptions<TenantConnectionOptions> options,
            ILogger<TenantProvisioningService> logger,
            PlatformUserDirectoryService directory)
        {
            _platformDb = platformDb;
            _connectionFactory = connectionFactory;
            _scopeFactory = scopeFactory;
            _options = options;
            _logger = logger;
            _directory = directory;
        }

        public async Task<Tenant> CreateTenantAsync(string name, string subdomain, string? region)
        {
            subdomain = subdomain.Trim().ToLowerInvariant();
            var databaseName = _connectionFactory.NormalizeDatabaseName($"vaultex_{subdomain}");
            var connectionString = _connectionFactory.BuildForNewTenant(subdomain);

            var tenant = new Tenant
            {
                Name = name,
                Subdomain = subdomain,
                Region = region ?? "",
                Status = TenantStatus.Active,
                DatabaseName = databaseName,
                ConnectionString = connectionString,
                Configuration = new TenantConfiguration()
            };

            _platformDb.Tenants.Add(tenant);
            await _platformDb.SaveChangesAsync();

            try
            {
                await ProvisionTenantDatabaseAsync(tenant);
            }
            catch
            {
                tenant.Status = TenantStatus.Suspended;
                await _platformDb.SaveChangesAsync();
                throw;
            }

            return tenant;
        }

        public async Task ProvisionTenantDatabaseAsync(Tenant tenant)
        {
            var connectionString = _connectionFactory.GetConnectionString(tenant);

            if (_options.Value.ProvisionDatabases)
                await EnsureDatabaseExistsAsync(connectionString);

            using var scope = _scopeFactory.CreateScope();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenantContext.Set(tenant.Id, tenant.Subdomain, connectionString);

            var tenantDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await tenantDb.Database.MigrateAsync();
            await EnsureLocalTenantRecordAsync(tenantDb, tenant);
            await OpenIddictSeeder.SeedAsync(scope.ServiceProvider);
            await RoleSeeder.SeedRolesAsync(scope.ServiceProvider, tenant.Id);
            await SyncDirectoryAsync(tenantDb, tenant.Id);
        }

        public async Task<User> CreateTenantAdminAsync(Guid tenantId, string email, string password, string? fullName)
        {
            var tenant = await _platformDb.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == tenantId);

            if (tenant is null)
                throw new InvalidOperationException("Tenant not found.");

            using var scope = _scopeFactory.CreateScope();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenantContext.Set(tenant.Id, tenant.Subdomain, _connectionFactory.GetConnectionString(tenant));

            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var existing = await userManager.FindByEmailAsync(email);
            if (existing is not null)
                throw new InvalidOperationException("Email already in use.");

            var user = new User
            {
                UserName = email,
                Email = email,
                FullName = fullName,
                TenantId = tenant.Id,
                IsActive = true,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
                throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));

            await userManager.AddToRoleAsync(user, $"{tenant.Id}:{Roles.Admin}");
            await _directory.UpsertAsync(tenant.Id, user.Email!, user.Id, user.IsActive);
            return user;
        }

        public async Task<int> CountTenantUsersAsync(Tenant tenant)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
                tenantContext.Set(tenant.Id, tenant.Subdomain, _connectionFactory.GetConnectionString(tenant));

                var tenantDb = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
                return await tenantDb.Set<User>().CountAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to count users for tenant {TenantId}", tenant.Id);
                return 0;
            }
        }

        private async Task EnsureDatabaseExistsAsync(string tenantConnectionString)
        {
            var tenantBuilder = new NpgsqlConnectionStringBuilder(tenantConnectionString);
            var databaseName = tenantBuilder.Database;
            if (string.IsNullOrWhiteSpace(databaseName))
                throw new InvalidOperationException("Tenant database name is missing.");

            var adminBuilder = new NpgsqlConnectionStringBuilder(tenantConnectionString)
            {
                Database = "postgres"
            };

            await using var connection = new NpgsqlConnection(adminBuilder.ConnectionString);
            await connection.OpenAsync();

            await using (var existsCommand = connection.CreateCommand())
            {
                existsCommand.CommandText = "select 1 from pg_database where datname = @databaseName";
                existsCommand.Parameters.AddWithValue("databaseName", databaseName);
                var exists = await existsCommand.ExecuteScalarAsync();
                if (exists is not null)
                    return;
            }

            var quotedName = new NpgsqlCommandBuilder().QuoteIdentifier(databaseName);
            await using var createCommand = connection.CreateCommand();
            createCommand.CommandText = $"create database {quotedName}";
            await createCommand.ExecuteNonQueryAsync();

            _logger.LogInformation("Provisioned tenant database {DatabaseName}", databaseName);
        }

        private static async Task EnsureLocalTenantRecordAsync(AppDbContext tenantDb, Tenant tenant)
        {
            var existing = await tenantDb.Set<Tenant>().IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == tenant.Id);

            if (existing is null)
            {
                tenantDb.Set<Tenant>().Add(new Tenant
                {
                    Id = tenant.Id,
                    Name = tenant.Name,
                    Subdomain = tenant.Subdomain,
                    Region = tenant.Region,
                    Status = tenant.Status,
                    DatabaseName = tenant.DatabaseName,
                    ConnectionString = tenant.ConnectionString,
                    CreatedAt = tenant.CreatedAt,
                    Configuration = new TenantConfiguration
                    {
                        TenantId = tenant.Id
                    }
                });
            }
            else
            {
                existing.Name = tenant.Name;
                existing.Region = tenant.Region;
                existing.Status = tenant.Status;
                existing.DatabaseName = tenant.DatabaseName;
                existing.ConnectionString = tenant.ConnectionString;
            }

            await tenantDb.SaveChangesAsync();
        }

        private async Task SyncDirectoryAsync(AppDbContext tenantDb, Guid tenantId)
        {
            var users = await tenantDb.Users
                .IgnoreQueryFilters()
                .Where(u => u.TenantId == tenantId && u.Email != null)
                .Select(u => new { u.Id, u.Email, u.IsActive })
                .ToListAsync();

            foreach (var user in users)
                await _directory.UpsertAsync(tenantId, user.Email!, user.Id, user.IsActive);
        }
    }
}
