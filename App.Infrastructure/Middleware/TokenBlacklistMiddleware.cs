using System.Text.Json;
using App.Infrastructure.Services;
using Microsoft.AspNetCore.Http;

namespace App.Infrastructure.Middleware
{
    public class TokenBlacklistMiddleware
    {
        private readonly RequestDelegate _next;

        public TokenBlacklistMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, TokenBlacklistService blacklist)
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                var jti = context.User.FindFirst("jti")?.Value;
                if (!string.IsNullOrEmpty(jti) && await blacklist.IsBlacklistedAsync(jti))
                {
                    context.Response.StatusCode = 401;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync(JsonSerializer.Serialize(new
                    {
                        error = "invalid_token",
                        error_description = "The token has been revoked."
                    }));
                    return;
                }
            }

            await _next(context);
        }
    }
}
