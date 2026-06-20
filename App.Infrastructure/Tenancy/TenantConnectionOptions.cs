namespace App.Infrastructure.Tenancy
{
    public class TenantConnectionOptions
    {
        public bool ProvisionDatabases { get; set; } = true;
        public string? ConnectionTemplate { get; set; }
    }
}
