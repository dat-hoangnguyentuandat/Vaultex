namespace App.Infrastructure.Tenancy
{
    public interface ITenantContext
    {
        Guid? TenantId { get; }
        string? Subdomain { get; }
        bool IsResolved { get; }
        void Set(Guid tenantId, string subdomain);
    }
}
