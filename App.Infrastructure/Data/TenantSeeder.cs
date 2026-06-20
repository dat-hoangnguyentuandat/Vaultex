using App.Domain.Constants;
using App.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Data
{
    public static class TenantSeeder
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            var platformDb = serviceProvider.GetRequiredService<PlatformDbContext>();
            var userManager = serviceProvider.GetRequiredService<UserManager<User>>();
            var roleManager = serviceProvider.GetRequiredService<RoleManager<AppRole>>();
            var logger = serviceProvider.GetRequiredService<ILogger<AppDbContext>>();
            var provisioning = serviceProvider.GetRequiredService<App.Infrastructure.Services.TenantProvisioningService>();
            var connectionFactory = serviceProvider.GetRequiredService<App.Infrastructure.Tenancy.TenantConnectionStringFactory>();

            // Seed platform-level role (no tenant)
            var platformRoleName = Roles.PlatformAdmin;
            if (await roleManager.FindByNameAsync(platformRoleName) is null)
            {
                await roleManager.CreateAsync(new AppRole
                {
                    Name = platformRoleName,
                    NormalizedName = platformRoleName.ToUpperInvariant(),
                    TenantId = null
                });
            }

            // Seed platform admin user (no tenant)
            var platformAdminEmail = "platform@vaultex.io";
            if (await userManager.FindByEmailAsync(platformAdminEmail) is null)
            {
                var platformAdmin = new User
                {
                    UserName = platformAdminEmail,
                    Email = platformAdminEmail,
                    FullName = "Platform Administrator",
                    TenantId = null,
                    IsActive = true,
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(platformAdmin, "Platform@123456");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(platformAdmin, platformRoleName);
                    logger.LogInformation("Seeded platform admin: {Email}", platformAdminEmail);
                }
                else
                {
                    logger.LogError("Failed to seed platform admin: {Errors}",
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }

            // Seed dev tenant
            var subdomain = "acme";
            var tenant = await platformDb.Tenants.FirstOrDefaultAsync(t => t.Subdomain == subdomain);

            if (tenant is null)
            {
                tenant = new Tenant
                {
                    Name = "Acme Corporation",
                    Subdomain = subdomain,
                    Region = "Southeast Asia",
                    Status = TenantStatus.Active,
                    DatabaseName = connectionFactory.NormalizeDatabaseName($"vaultex_{subdomain}"),
                    ConnectionString = connectionFactory.BuildForNewTenant(subdomain),
                    Configuration = new TenantConfiguration()
                };
                platformDb.Tenants.Add(tenant);
                await platformDb.SaveChangesAsync();
                logger.LogInformation("Seeded tenant: {Subdomain} ({TenantId})", subdomain, tenant.Id);
            }

            await provisioning.ProvisionTenantDatabaseAsync(tenant);

            // Seed tenant admin user
            var adminEmail = "admin@acme.com";
            try
            {
                await provisioning.CreateTenantAdminAsync(tenant.Id, adminEmail, "Admin@123456", "Acme Super Admin");
                logger.LogInformation("Seeded tenant admin: {Email}", adminEmail);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Email already", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("Tenant admin already exists: {Email}", adminEmail);
            }
        }
    }
}
