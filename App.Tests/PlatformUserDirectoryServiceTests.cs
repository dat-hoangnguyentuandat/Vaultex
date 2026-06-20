using App.Domain.Entities;
using App.Infrastructure.Data;
using App.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

namespace App.Tests;

public class PlatformUserDirectoryServiceTests : IDisposable
{
    private readonly PlatformDbContext _db;
    private readonly PlatformUserDirectoryService _svc;

    public PlatformUserDirectoryServiceTests()
    {
        var options = new DbContextOptionsBuilder<PlatformDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new PlatformDbContext(options);
        _svc = new PlatformUserDirectoryService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task ResolveSingleTenantAsync_OneActiveMatch_ReturnsTenant()
    {
        var tenant = new Tenant { Name = "Acme", Subdomain = "acme", Status = TenantStatus.Active };
        _db.Tenants.Add(tenant);
        await _db.SaveChangesAsync();

        await _svc.UpsertAsync(tenant.Id, "admin@acme.com", Guid.NewGuid());

        var resolved = await _svc.ResolveSingleTenantAsync("ADMIN@ACME.COM");

        Assert.NotNull(resolved);
        Assert.Equal(tenant.Id, resolved.Id);
    }

    [Fact]
    public async Task ResolveSingleTenantAsync_MultipleMatches_ReturnsNull()
    {
        var acme = new Tenant { Name = "Acme", Subdomain = "acme", Status = TenantStatus.Active };
        var beta = new Tenant { Name = "Beta", Subdomain = "beta", Status = TenantStatus.Active };
        _db.Tenants.AddRange(acme, beta);
        await _db.SaveChangesAsync();

        await _svc.UpsertAsync(acme.Id, "shared@example.com", Guid.NewGuid());
        await _svc.UpsertAsync(beta.Id, "shared@example.com", Guid.NewGuid());

        var resolved = await _svc.ResolveSingleTenantAsync("shared@example.com");

        Assert.Null(resolved);
    }
}
