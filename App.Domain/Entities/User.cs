using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;

namespace App.Domain.Entities
{
    public class User : IdentityUser<Guid>
    {
        public Guid? TenantId { get; set; }
        public Tenant? Tenant { get; set; }

        public string? FullName { get; set; }
        public DateOnly? DateOfBirth { get; set; }
        public string? Company { get; set; }
        public string? Position { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginAt { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
