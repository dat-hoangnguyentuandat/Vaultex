using App.Domain.Entities;
using App.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Data
{
    public class AppDbContext : IdentityDbContext<User, AppRole, Guid>
    {
        private readonly ITenantContext? _tenantContext;

        public AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext? tenantContext = null)
            : base(options)
        {
            _tenantContext = tenantContext;
        }

        public DbSet<Tenant> Tenants { get; set; }
        public DbSet<TenantConfiguration> TenantConfigurations { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Policy> Policies { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);
            builder.UseOpenIddict();

            builder.Entity<Tenant>(e =>
            {
                e.HasIndex(t => t.Subdomain).IsUnique();
                e.HasOne(t => t.Configuration)
                 .WithOne(c => c.Tenant)
                 .HasForeignKey<TenantConfiguration>(c => c.TenantId);
            });

            builder.Entity<User>(e =>
            {
                e.HasOne(u => u.Tenant)
                 .WithMany(t => t.Users)
                 .HasForeignKey(u => u.TenantId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<AppRole>(e =>
            {
                e.HasOne(r => r.Tenant)
                 .WithMany(t => t.Roles)
                 .HasForeignKey(r => r.TenantId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // Global query filters — auto-scope all queries to current tenant
            var tenantId = _tenantContext?.TenantId;

            builder.Entity<User>()
                .HasQueryFilter(u => !_tenantContext!.IsResolved || u.TenantId == _tenantContext.TenantId);

            builder.Entity<AppRole>()
                .HasQueryFilter(r => !_tenantContext!.IsResolved || r.TenantId == _tenantContext.TenantId);

            builder.Entity<Product>()
                .HasQueryFilter(p => !_tenantContext!.IsResolved || p.TenantId == _tenantContext.TenantId);

            builder.Entity<Policy>(e =>
            {
                e.HasOne(p => p.Tenant)
                 .WithMany()
                 .HasForeignKey(p => p.TenantId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            builder.Entity<Policy>()
                .HasQueryFilter(p => !_tenantContext!.IsResolved || p.TenantId == _tenantContext.TenantId);

            // AuditLog: immutable, no update/delete. Tenant-scoped for tenant admins.
            builder.Entity<AuditLog>(e =>
            {
                e.HasIndex(a => new { a.TenantId, a.Timestamp });
                e.HasIndex(a => new { a.TenantId, a.UserId });
                e.HasIndex(a => a.EventType);
            });
        }
    }
}
