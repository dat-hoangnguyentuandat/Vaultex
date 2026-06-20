using App.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Data
{
    public class PlatformDbContext : DbContext
    {
        public PlatformDbContext(DbContextOptions<PlatformDbContext> options)
            : base(options)
        {
        }

        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<TenantConfiguration> TenantConfigurations { get; set; }
        public DbSet<PlatformUserDirectoryEntry> UserDirectory { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Tenant>(e =>
            {
                e.HasIndex(t => t.Subdomain).IsUnique();
                e.Property(t => t.Subdomain).IsRequired();
                e.HasOne(t => t.Configuration)
                 .WithOne(c => c.Tenant)
                 .HasForeignKey<TenantConfiguration>(c => c.TenantId);
            });

            builder.Entity<PlatformUserDirectoryEntry>(e =>
            {
                e.HasOne(d => d.Tenant)
                 .WithMany()
                 .HasForeignKey(d => d.TenantId)
                 .OnDelete(DeleteBehavior.Cascade);
                e.HasIndex(d => d.NormalizedEmail);
                e.HasIndex(d => new { d.TenantId, d.NormalizedEmail }).IsUnique();
            });
        }
    }
}
