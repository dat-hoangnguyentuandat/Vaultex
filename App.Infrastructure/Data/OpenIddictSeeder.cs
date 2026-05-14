using App.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace App.Infrastructure.Data
{
    public static class OpenIddictSeeder
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var settings = configuration.GetSection("OpenIddict").Get<OpenIddictSettings>() ?? new();

            var manager = serviceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            var scopeManager = serviceProvider.GetRequiredService<IOpenIddictScopeManager>();

            foreach (var scope in settings.Scopes)
            {
                if (await scopeManager.FindByNameAsync(scope.Name) is null)
                {
                    await scopeManager.CreateAsync(new OpenIddictScopeDescriptor
                    {
                        Name = scope.Name,
                        DisplayName = scope.DisplayName
                    });
                }
            }

            foreach (var (_, appSettings) in settings.Applications)
            {
                if (await manager.FindByClientIdAsync(appSettings.ClientId) is not null)
                    continue;

                var descriptor = new OpenIddictApplicationDescriptor
                {
                    ClientId = appSettings.ClientId,
                    ClientSecret = appSettings.ClientSecret,
                    DisplayName = appSettings.DisplayName,
                };

                foreach (var uri in appSettings.RedirectUris)
                    descriptor.RedirectUris.Add(new Uri(uri));

                foreach (var uri in appSettings.PostLogoutRedirectUris)
                    descriptor.PostLogoutRedirectUris.Add(new Uri(uri));

                foreach (var permission in appSettings.Permissions)
                    descriptor.Permissions.Add(permission);

                foreach (var requirement in appSettings.Requirements)
                    descriptor.Requirements.Add(requirement);

                await manager.CreateAsync(descriptor);
            }
        }
    }
}
