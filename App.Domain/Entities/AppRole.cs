using Microsoft.AspNetCore.Identity;

namespace App.Domain.Entities
{
    public class AppRole : IdentityRole<Guid>
    {
        public Guid? TenantId { get; set; }
        public Tenant? Tenant { get; set; }
    }
}
