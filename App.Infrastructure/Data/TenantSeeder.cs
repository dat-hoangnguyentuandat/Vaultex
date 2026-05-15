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
            var roleManager = serviceProvider.GetRequiredService<RoleManager<AppRole>>();
            var logger = serviceProvider.GetRequiredService<ILogger<AppDbContext>>();

            // Seed platform-level role (no tenant)
            var platformRoleName = Roles.PlatformAdmin;
            if (await roleManager.FindByNameAsync(platformRoleName) is null)
            {
                await roleManager.CreateAsync(new AppRole
                {
                    Name = platformRoleName,
                    NormalizedName = platformRoleName.ToUpperInvariant(),
                    TenantId = null
                });
            }

            // Seed platform admin user (no tenant)
            var platformAdminEmail = "platform@vaultex.io";
            if (await userManager.FindByEmailAsync(platformAdminEmail) is null)
            {
                var platformAdmin = new User
                {
                    UserName = platformAdminEmail,
                    Email = platformAdminEmail,
                    FullName = "Platform Administrator",
                    TenantId = null,
                    IsActive = true,
                    EmailConfirmed = true
                };

                var result = await userManager.CreateAsync(platformAdmin, "Platform@123456");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(platformAdmin, platformRoleName);
                    logger.LogInformation("Seeded platform admin: {Email}", platformAdminEmail);
                }
                else
                {
                    logger.LogError("Failed to seed platform admin: {Errors}",
                        string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }

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

            // Seed tenant admin user
            var adminEmail = "admin@acme.com";
            if (await userManager.FindByEmailAsync(adminEmail) is null)
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
                    await userManager.AddToRoleAsync(admin, $"{tenant.Id}:{Roles.Admin}");
                    logger.LogInformation("Seeded tenant admin: {Email}", adminEmail);
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
