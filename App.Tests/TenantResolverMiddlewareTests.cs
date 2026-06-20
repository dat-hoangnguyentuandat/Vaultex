using App.Domain.Entities;
using App.Infrastructure.Data;
using App.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace App.Tests;

public class TenantResolverMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_HeaderTenant_ResolvesActiveTenantConnection()
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Acme",
            Subdomain = "acme",
            Status = TenantStatus.Active,
            ConnectionString = "Host=localhost;Database=tenant_acme"
        };

        await using var db = CreatePlatformDb();
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant"] = "acme";
        var tenantContext = new TenantContext();

        var middleware = new TenantResolverMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, tenantContext, db, CreateConnectionFactory());

        Assert.True(tenantContext.IsResolved);
        Assert.Equal(tenant.Id, tenantContext.TenantId);
        Assert.Equal("acme", tenantContext.Subdomain);
        Assert.Equal(tenant.ConnectionString, tenantContext.ConnectionString);
    }

    [Fact]
    public async Task InvokeAsync_SuspendedTenant_DoesNotResolve()
    {
        await using var db = CreatePlatformDb();
        db.Tenants.Add(new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Acme",
            Subdomain = "acme",
            Status = TenantStatus.Suspended,
            ConnectionString = "Host=localhost;Database=tenant_acme"
        });
        await db.SaveChangesAsync();

        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant"] = "acme";
        var tenantContext = new TenantContext();

        var middleware = new TenantResolverMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, tenantContext, db, CreateConnectionFactory());

        Assert.False(tenantContext.IsResolved);
        Assert.Null(tenantContext.TenantId);
        Assert.Null(tenantContext.ConnectionString);
    }

    [Fact]
    public async Task InvokeAsync_QueryTenant_ResolvesActiveTenantConnection()
    {
        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            Name = "Acme",
            Subdomain = "acme",
            Status = TenantStatus.Active,
            ConnectionString = "Host=localhost;Database=tenant_acme"
        };

        await using var db = CreatePlatformDb();
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?tenant=acme");
        var tenantContext = new TenantContext();

        var middleware = new TenantResolverMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context, tenantContext, db, CreateConnectionFactory());

        Assert.True(tenantContext.IsResolved);
        Assert.Equal(tenant.Id, tenantContext.TenantId);
    }

    private static PlatformDbContext CreatePlatformDb()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new PlatformDbContext(options);
    }

    private static TenantConnectionStringFactory CreateConnectionFactory()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=default"
            })
            .Build();

        return new TenantConnectionStringFactory(config);
    }
}
