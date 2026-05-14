using App.Domain.Constants;
using App.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Data
{
    public static class TenantSeeder
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            var db = serviceProvider.GetRequiredService<AppDbContext>();
            var userManager = serviceProvider.GetRequiredService<UserManager<User>>();
            var logger = serviceProvider.GetRequiredService<ILogger<AppDbContext>>();

            // Seed dev tenant
            var subdomain = "acme";
            var tenant = await db.Tenants.FirstOrDefaultAsync(t => t.Subdomain == subdomain);

            if (tenant is null)
            {
                tenant = new Tenant
                {
                    Name = "Acme Corporation",
                    Subdomain = subdomain,
                    Region = "Southeast Asia",
                    Status = TenantStatus.Active,
                    Configuration = new TenantConfiguration()
                };
                db.Tenants.Add(tenant);
                await db.SaveChangesAsync();
                logger.LogInformation("Seeded tenant: {Subdomain} ({TenantId})", subdomain, tenant.Id);
            }

            // Seed roles for this tenant
            await RoleSeeder.SeedRolesAsync(serviceProvider, tenant.Id);

            // Seed super admin user
            var adminEmail = "admin@acme.com";
            var existingAdmin = await userManager.FindByEmailAsync(adminEmail);

            if (existingAdmin is null)
            {
                var admin = new User
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    FullName = "Acme Super Admin",
                    TenantId = tenant.Id,
                    IsActive = true,
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(admin, "Admin@123456");
                if (result.Succeeded)
                {
                    var adminRoleName = $"{tenant.Id}:{Roles.Admin}";
                    await userManager.AddToRoleAsync(admin, adminRoleName);
                    logger.LogInformation("Seeded super admin: {Email}", adminEmail);
                }
                else
                {
                    logger.LogError("Failed to seed admin user: {Errors}",
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
        }
    }
}
