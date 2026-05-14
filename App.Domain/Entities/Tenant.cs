namespace App.Domain.Entities
{
    public class Tenant
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "";
        public string Subdomain { get; set; } = "";
        public string Region { get; set; } = "";
        public TenantStatus Status { get; set; } = TenantStatus.Active;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public TenantConfiguration? Configuration { get; set; }
        public ICollection<User> Users { get; set; } = [];
        public ICollection<AppRole> Roles { get; set; } = [];
    }

    public enum TenantStatus
    {
        Active,
        Suspended,
        Deleted
    }
}
