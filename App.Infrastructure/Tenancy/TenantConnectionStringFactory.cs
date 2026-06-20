using App.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace App.Infrastructure.Tenancy
{
    public class TenantConnectionStringFactory
    {
        private readonly IConfiguration _configuration;

        public TenantConnectionStringFactory(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string GetPlatformConnectionString()
            => _configuration.GetConnectionString("PlatformConnection")
               ?? _configuration.GetConnectionString("DefaultConnection")
               ?? throw new InvalidOperationException("PlatformConnection is not configured.");

        public string GetDefaultTenantConnectionString()
            => _configuration.GetConnectionString("DefaultTenantConnection")
               ?? _configuration.GetConnectionString("DefaultConnection")
               ?? throw new InvalidOperationException("DefaultTenantConnection is not configured.");

        public string GetConnectionString(Tenant tenant)
        {
            if (!string.IsNullOrWhiteSpace(tenant.ConnectionString))
                return tenant.ConnectionString;

            if (!string.IsNullOrWhiteSpace(tenant.DatabaseName))
                return BuildFromDatabaseName(tenant.DatabaseName);

            return GetDefaultTenantConnectionString();
        }

        public string BuildForNewTenant(string subdomain)
        {
            var databaseName = NormalizeDatabaseName($"vaultex_{subdomain}");
            return BuildFromDatabaseName(databaseName);
        }

        public string NormalizeDatabaseName(string value)
        {
            var normalized = new string(value.ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '_')
                .ToArray())
                .Trim('_');

            return string.IsNullOrWhiteSpace(normalized) ? $"vaultex_{Guid.NewGuid():N}" : normalized;
        }

        private string BuildFromDatabaseName(string databaseName)
        {
            var template = _configuration["Tenancy:ConnectionTemplate"];
            var baseConnection = string.IsNullOrWhiteSpace(template)
                ? GetDefaultTenantConnectionString()
                : template;

            var builder = new NpgsqlConnectionStringBuilder(baseConnection)
            {
                Database = databaseName
            };

            return builder.ConnectionString;
        }
    }
}
