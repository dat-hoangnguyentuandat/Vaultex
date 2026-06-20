namespace App.Domain.Entities
{
    public class PlatformUserDirectoryEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid TenantId { get; set; }
        public string Email { get; set; } = "";
        public string NormalizedEmail { get; set; } = "";
        public Guid? UserId { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public Tenant Tenant { get; set; } = null!;
    }
}
