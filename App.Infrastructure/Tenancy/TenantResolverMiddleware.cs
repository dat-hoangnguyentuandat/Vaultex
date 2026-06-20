using App.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace App.Infrastructure.Tenancy
{
    public class TenantResolverMiddleware
    {
        private readonly RequestDelegate _next;

        public TenantResolverMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(
            HttpContext context,
            ITenantContext tenantContext,
            PlatformDbContext platformDb,
            TenantConnectionStringFactory connectionFactory)
        {
            var subdomain = await ResolveSubdomainAsync(context);

            if (!string.IsNullOrEmpty(subdomain))
            {
                var tenant = await platformDb.Tenants
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Subdomain == subdomain && t.Status == App.Domain.Entities.TenantStatus.Active);

                if (tenant is not null)
                    tenantContext.Set(tenant.Id, tenant.Subdomain, connectionFactory.GetConnectionString(tenant));
            }

            await _next(context);
        }

        private static async Task<string?> ResolveSubdomainAsync(HttpContext context)
        {
            // Header override (useful for dev/testing: X-Tenant: acme)
            if (context.Request.Headers.TryGetValue("X-Tenant", out var headerValue))
                return headerValue.ToString().ToLowerInvariant();

            if (context.Request.Query.TryGetValue("tenant", out var queryValue))
                return queryValue.ToString().ToLowerInvariant();

            if (context.Request.HasFormContentType)
            {
                context.Request.EnableBuffering();
                var form = await context.Request.ReadFormAsync();
                context.Request.Body.Position = 0;

                if (form.TryGetValue("tenant", out var formValue)
                    && !string.IsNullOrWhiteSpace(formValue.ToString()))
                    return formValue.ToString().ToLowerInvariant();

                if (form.TryGetValue("redirect_uri", out var redirectUriValue)
                    && Uri.TryCreate(redirectUriValue.ToString(), UriKind.Absolute, out var redirectUri))
                {
                    var redirectQuery = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(redirectUri.Query);
                    if (redirectQuery.TryGetValue("tenant", out var redirectTenant)
                        && !string.IsNullOrWhiteSpace(redirectTenant.ToString()))
                        return redirectTenant.ToString().ToLowerInvariant();
                }
            }

            // Subdomain: acme.vaultex.io → "acme"
            var host = context.Request.Host.Host;
            var parts = host.Split('.');
            if (parts.Length >= 3)
                return parts[0].ToLowerInvariant();

            if (parts.Length == 2 && parts[1].Equals("localhost", StringComparison.OrdinalIgnoreCase))
                return parts[0].ToLowerInvariant();

            return null;
        }
    }
}
