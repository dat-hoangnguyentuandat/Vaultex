using App.Domain.Entities;
using App.Infrastructure.Data;
using App.Infrastructure.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace App.Tests;

public class AuditLogServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Mock<IHttpContextAccessor> _httpAccessor;
    private readonly AuditLogService _svc;

    public AuditLogServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _db = new AppDbContext(options);

        _httpAccessor = new Mock<IHttpContextAccessor>();

        _svc = new AuditLogService(_db, _httpAccessor.Object, new Mock<ILogger<AuditLogService>>().Object);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task LogAsync_PersistsEntry_WithRequiredFields()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        await _svc.LogAsync("LOGIN_SUCCESS", tenantId, userId);

        var entry = await _db.AuditLogs.SingleAsync();
        Assert.Equal("LOGIN_SUCCESS", entry.EventType);
        Assert.Equal(tenantId, entry.TenantId);
        Assert.Equal(userId, entry.UserId);
        Assert.True(entry.Timestamp <= DateTime.UtcNow);
        Assert.True(entry.Timestamp > DateTime.UtcNow.AddSeconds(-5));
    }

    [Fact]
    public async Task LogAsync_WithResourceInfo_PersistsResourceFields()
    {
        var resourceId = Guid.NewGuid().ToString();

        await _svc.LogAsync("PRODUCT_CREATE",
            resourceType: "Product",
            resourceId: resourceId,
            newValue: "{\"name\":\"Widget\"}");

        var entry = await _db.AuditLogs.SingleAsync();
        Assert.Equal("Product", entry.ResourceType);
        Assert.Equal(resourceId, entry.ResourceId);
        Assert.Equal("{\"name\":\"Widget\"}", entry.NewValue);
        Assert.Null(entry.OldValue);
    }

    [Fact]
    public async Task LogAsync_WithHttpContext_CapturesIpAndUserAgent()
    {
        var mockConnection = new Mock<ConnectionInfo>();
        mockConnection.Setup(c => c.RemoteIpAddress)
            .Returns(System.Net.IPAddress.Parse("10.0.0.42"));

        var mockRequest = new Mock<HttpRequest>();
        var headers = new HeaderDictionary { ["User-Agent"] = "TestAgent/1.0" };
        mockRequest.Setup(r => r.Headers).Returns(headers);

        var mockCtx = new Mock<HttpContext>();
        mockCtx.Setup(c => c.Connection).Returns(mockConnection.Object);
        mockCtx.Setup(c => c.Request).Returns(mockRequest.Object);

        _httpAccessor.Setup(a => a.HttpContext).Returns(mockCtx.Object);

        await _svc.LogAsync("LOGIN_SUCCESS");

        var entry = await _db.AuditLogs.SingleAsync();
        Assert.Equal("10.0.0.42", entry.IpAddress);
        Assert.Equal("TestAgent/1.0", entry.UserAgent);
    }

    [Fact]
    public async Task LogAsync_MultipleEvents_AllPersisted()
    {
        await _svc.LogAsync("LOGIN_SUCCESS");
        await _svc.LogAsync("LOGIN_FAILED");
        await _svc.LogAsync("LOGOUT");

        Assert.Equal(3, await _db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task LogAsync_NullOptionalFields_DoesNotThrow()
    {
        await _svc.LogAsync("ACCESS_DENIED");

        var entry = await _db.AuditLogs.SingleAsync();
        Assert.Equal("ACCESS_DENIED", entry.EventType);
        Assert.Null(entry.TenantId);
        Assert.Null(entry.UserId);
        Assert.Null(entry.ResourceType);
        Assert.Null(entry.IpAddress);
    }
}
