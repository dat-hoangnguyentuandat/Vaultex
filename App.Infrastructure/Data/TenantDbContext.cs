using App.Domain.Entities;
using App.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Data
{
    public class TenantDbContext : IdentityDbContext<User, AppRole, Guid>
    {
        private readonly ITenantContext? _tenantContext;
        private bool TenantFilterEnabled => _tenantContext?.IsResolved == true;
        private Guid? CurrentTenantId => _tenantContext?.TenantId;

        public TenantDbContext(DbContextOptions options, ITenantContext? tenantContext = null)
            : base(options)
        {
            _tenantContext = tenantContext;
        }

        public DbSet<Policy> Policies { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<TenantConfiguration> TenantConfigurations { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.UseOpenIddict();

            builder.Entity<User>(e =>
            {
                e.HasOne(u => u.Tenant)
                 .WithMany(t => t.Users)
                 .HasForeignKey(u => u.TenantId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<Tenant>(e =>
            {
                e.HasIndex(t => t.Subdomain).IsUnique();
                e.HasOne(t => t.Configuration)
                 .WithOne(c => c.Tenant)
                 .HasForeignKey<TenantConfiguration>(c => c.TenantId);
            });

            builder.Entity<AppRole>(e =>
            {
                e.HasOne(r => r.Tenant)
                 .WithMany(t => t.Roles)
                 .HasForeignKey(r => r.TenantId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<Policy>(e =>
            {
                e.HasOne(p => p.Tenant)
                 .WithMany()
                 .HasForeignKey(p => p.TenantId)
                 .OnDelete(DeleteBehavior.Restrict);
                e.HasIndex(p => p.TenantId);
            });

            builder.Entity<AuditLog>(e =>
            {
                e.HasIndex(a => new { a.TenantId, a.Timestamp });
                e.HasIndex(a => new { a.TenantId, a.UserId });
                e.HasIndex(a => a.EventType);
            });

            // Keep tenant filters as a defense-in-depth layer for migrated/shared data.
            builder.Entity<User>()
                .HasQueryFilter(u => !TenantFilterEnabled || u.TenantId == CurrentTenantId);

            builder.Entity<AppRole>()
                .HasQueryFilter(r => !TenantFilterEnabled || r.TenantId == CurrentTenantId);

            builder.Entity<Policy>()
                .HasQueryFilter(p => !TenantFilterEnabled || p.TenantId == CurrentTenantId);

            builder.Entity<AuditLog>()
                .HasQueryFilter(a => !TenantFilterEnabled || a.TenantId == CurrentTenantId);
        }
    }
}
