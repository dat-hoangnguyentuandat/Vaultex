using App.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace App.Infrastructure.Tenancy
{
    public class TenantResolverMiddleware
    {
        private readonly RequestDelegate _next;

        public TenantResolverMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, AppDbContext db)
        {
            var subdomain = ResolveSubdomain(context);

            if (!string.IsNullOrEmpty(subdomain))
            {
                var tenant = await db.Tenants
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Subdomain == subdomain);

                if (tenant is not null)
                    tenantContext.Set(tenant.Id, tenant.Subdomain);
            }

            await _next(context);
        }

        private static string? ResolveSubdomain(HttpContext context)
        {
            // Header override (useful for dev/testing: X-Tenant: acme)
            if (context.Request.Headers.TryGetValue("X-Tenant", out var headerValue))
                return headerValue.ToString().ToLowerInvariant();

            // Subdomain: acme.vaultex.io → "acme"
            var host = context.Request.Host.Host;
            var parts = host.Split('.');
            if (parts.Length >= 3)
                return parts[0].ToLowerInvariant();

            return null;
        }
    }
}
