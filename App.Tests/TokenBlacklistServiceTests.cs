using App.Infrastructure.Services;
using Moq;
using StackExchange.Redis;

namespace App.Tests;

public class TokenBlacklistServiceTests
{
    // ── no Redis (graceful degrade) ──────────────────────────────────────────

    [Fact]
    public async Task BlacklistAsync_NoRedis_DoesNotThrow()
    {
        var svc = new TokenBlacklistService(null);
        await svc.BlacklistAsync("token-1", DateTimeOffset.UtcNow.AddHours(1));
        // no exception = pass
    }

    [Fact]
    public async Task IsBlacklistedAsync_NoRedis_ReturnsFalse()
    {
        var svc = new TokenBlacklistService(null);
        Assert.False(await svc.IsBlacklistedAsync("token-1"));
    }

    // ── with Redis ───────────────────────────────────────────────────────────

    private static (TokenBlacklistService svc, Mock<IDatabase> dbMock) BuildWithMockRedis()
    {
        var dbMock = new Mock<IDatabase>();
        var redisMock = new Mock<IConnectionMultiplexer>();
        redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);
        return (new TokenBlacklistService(redisMock.Object), dbMock);
    }

    [Fact]
    public async Task BlacklistAsync_ValidExpiry_CallsStringSet()
    {
        var (svc, dbMock) = BuildWithMockRedis();
        dbMock.Setup(d => d.StringSetAsync(
            It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(), It.IsAny<bool>(),
            It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        await svc.BlacklistAsync("jti-abc", DateTimeOffset.UtcNow.AddHours(1));

        dbMock.Verify(d => d.StringSetAsync(
            "blacklist:jti-abc", "1",
            It.Is<TimeSpan?>(t => t.HasValue && t.Value > TimeSpan.Zero),
            It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task BlacklistAsync_AlreadyExpired_SkipsStringSet()
    {
        var (svc, dbMock) = BuildWithMockRedis();

        await svc.BlacklistAsync("jti-old", DateTimeOffset.UtcNow.AddSeconds(-1));

        dbMock.Verify(d => d.StringSetAsync(
            It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(), It.IsAny<bool>(),
            It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Never);
    }

    [Fact]
    public async Task IsBlacklistedAsync_KeyExists_ReturnsTrue()
    {
        var (svc, dbMock) = BuildWithMockRedis();
        dbMock.Setup(d => d.KeyExistsAsync("blacklist:jti-abc", It.IsAny<CommandFlags>()))
              .ReturnsAsync(true);

        Assert.True(await svc.IsBlacklistedAsync("jti-abc"));
    }

    [Fact]
    public async Task IsBlacklistedAsync_KeyMissing_ReturnsFalse()
    {
        var (svc, dbMock) = BuildWithMockRedis();
        dbMock.Setup(d => d.KeyExistsAsync("blacklist:jti-xyz", It.IsAny<CommandFlags>()))
              .ReturnsAsync(false);

        Assert.False(await svc.IsBlacklistedAsync("jti-xyz"));
    }

    [Fact]
    public async Task BlacklistAsync_KeyPrefixedCorrectly()
    {
        var (svc, dbMock) = BuildWithMockRedis();
        dbMock.Setup(d => d.StringSetAsync(
            It.IsAny<RedisKey>(), It.IsAny<RedisValue>(),
            It.IsAny<TimeSpan?>(), It.IsAny<bool>(),
            It.IsAny<When>(), It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        await svc.BlacklistAsync("my-token-id", DateTimeOffset.UtcNow.AddMinutes(30));

        dbMock.Verify(d => d.StringSetAsync(
            It.Is<RedisKey>(k => k == "blacklist:my-token-id"),
            It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(), It.IsAny<bool>(),
            It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
    }
}
