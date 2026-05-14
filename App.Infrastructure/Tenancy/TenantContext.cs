namespace App.Infrastructure.Tenancy
{
    public class TenantContext : ITenantContext
    {
        public Guid? TenantId { get; private set; }
        public string? Subdomain { get; private set; }
        public bool IsResolved { get; private set; }

        public void Set(Guid tenantId, string subdomain)
        {
            TenantId = tenantId;
            Subdomain = subdomain;
            IsResolved = true;
        }
    }
}
