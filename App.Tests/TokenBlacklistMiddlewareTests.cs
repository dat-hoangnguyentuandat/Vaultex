using App.Infrastructure.Middleware;
using App.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Moq;
using System.Security.Claims;
using StackExchange.Redis;

namespace App.Tests;

public class TokenBlacklistMiddlewareTests
{
    private static DefaultHttpContext BuildAuthenticatedContext(string? jti)
    {
        var claims = new List<Claim>();
        if (jti is not null)
            claims.Add(new Claim("jti", jti));

        var identity = new ClaimsIdentity(claims, "Bearer");
        var principal = new ClaimsPrincipal(identity);

        var ctx = new DefaultHttpContext();
        ctx.User = principal;
        ctx.Response.Body = new System.IO.MemoryStream();
        return ctx;
    }

    private static DefaultHttpContext BuildAnonymousContext()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new System.IO.MemoryStream();
        return ctx;
    }

    private static TokenBlacklistService BuildBlacklist(bool isBlacklisted)
    {
        var dbMock = new Mock<IDatabase>();
        dbMock.Setup(d => d.KeyExistsAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
              .ReturnsAsync(isBlacklisted);

        var redisMock = new Mock<IConnectionMultiplexer>();
        redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                 .Returns(dbMock.Object);

        return new TokenBlacklistService(redisMock.Object);
    }

    [Fact]
    public async Task AnonymousRequest_PassesThrough()
    {
        var nextCalled = false;
        var middleware = new TokenBlacklistMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var blacklist = new TokenBlacklistService(null);

        await middleware.InvokeAsync(BuildAnonymousContext(), blacklist);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task AuthenticatedRequest_NotBlacklisted_PassesThrough()
    {
        var nextCalled = false;
        var middleware = new TokenBlacklistMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var blacklist = BuildBlacklist(isBlacklisted: false);

        await middleware.InvokeAsync(BuildAuthenticatedContext("jti-valid"), blacklist);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task AuthenticatedRequest_BlacklistedToken_Returns401()
    {
        var nextCalled = false;
        var middleware = new TokenBlacklistMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var blacklist = BuildBlacklist(isBlacklisted: true);

        var ctx = BuildAuthenticatedContext("jti-revoked");
        await middleware.InvokeAsync(ctx, blacklist);

        Assert.False(nextCalled);
        Assert.Equal(401, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task AuthenticatedRequest_NoJti_PassesThrough()
    {
        var nextCalled = false;
        var middleware = new TokenBlacklistMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var blacklist = BuildBlacklist(isBlacklisted: false);

        // Authenticated but no jti claim
        await middleware.InvokeAsync(BuildAuthenticatedContext(null), blacklist);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task AuthenticatedRequest_NoRedis_PassesThrough()
    {
        var nextCalled = false;
        var middleware = new TokenBlacklistMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        var blacklist = new TokenBlacklistService(null); // no Redis

        await middleware.InvokeAsync(BuildAuthenticatedContext("jti-any"), blacklist);

        Assert.True(nextCalled);
    }
}
