using App.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Data
{
    // Compatibility context for the existing EF migrations.
    public class AppDbContext : TenantDbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext? tenantContext = null)
            : base(options, tenantContext)
        {
        }
    }
}
