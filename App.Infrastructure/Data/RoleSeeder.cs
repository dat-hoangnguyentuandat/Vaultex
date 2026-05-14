using App.Domain.Constants;
using App.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace App.Infrastructure.Data
{
    public static class RoleSeeder
    {
        public static async Task SeedRolesAsync(IServiceProvider serviceProvider, Guid tenantId)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<AppRole>>();

            foreach (var roleName in Roles.All)
            {
                var existing = await roleManager.FindByNameAsync($"{tenantId}:{roleName}");
                if (existing is null)
                {
                    var role = new AppRole
                    {
                        Name = $"{tenantId}:{roleName}",
                        NormalizedName = $"{tenantId}:{roleName}".ToUpperInvariant(),
                        TenantId = tenantId
                    };
                    await roleManager.CreateAsync(role);
                }
            }
        }
    }
}
