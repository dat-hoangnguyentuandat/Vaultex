namespace App.Infrastructure.Tenancy
{
    public interface ITenantContext
    {
        Guid? TenantId { get; }
        string? Subdomain { get; }
        string? ConnectionString { get; }
        bool IsResolved { get; }
        void Set(Guid tenantId, string subdomain, string connectionString);
    }
}
