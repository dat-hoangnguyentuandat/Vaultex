using App.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Tenancy
{
    public class TenantDiscoveryMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<TenantDiscoveryMiddleware> _logger;

        public TenantDiscoveryMiddleware(RequestDelegate next, ILogger<TenantDiscoveryMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context, PlatformUserDirectoryService directory)
        {
            if (ShouldDiscoverTenant(context))
            {
                context.Request.EnableBuffering();
                var form = await context.Request.ReadFormAsync();
                context.Request.Body.Position = 0;

                var accountType = form["accountType"].ToString();
                var email = form["Input.Email"].ToString();
                if (accountType != "tenant")
                {
                    await _next(context);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(email))
                {
                    var tenant = await directory.ResolveSingleTenantAsync(email);
                    if (tenant is not null)
                    {
                        context.Request.Headers["X-Tenant"] = tenant.Subdomain;
                        _logger.LogInformation("Resolved tenant {Subdomain} for tenant login email {Email}", tenant.Subdomain, email);
                    }
                    else
                    {
                        _logger.LogWarning("Unable to resolve a single active tenant for tenant login email {Email}", email);
                    }
                }
            }

            await _next(context);
        }

        private static bool ShouldDiscoverTenant(HttpContext context)
        {
            if (!HttpMethods.IsPost(context.Request.Method))
                return false;

            if (!context.Request.Path.Equals("/account/login", StringComparison.OrdinalIgnoreCase))
                return false;

            if (context.Request.Query.TryGetValue("tenant", out var tenantValue)
                && !string.IsNullOrWhiteSpace(tenantValue.ToString()))
                return false;

            if (context.Request.Headers.ContainsKey("X-Tenant"))
                return false;

            if (!context.Request.HasFormContentType)
                return false;

            return true;
        }
    }
}
