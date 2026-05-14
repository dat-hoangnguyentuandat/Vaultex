using StackExchange.Redis;

namespace App.Infrastructure.Services
{
    public class TokenBlacklistService
    {
        private readonly IConnectionMultiplexer? _redis;

        public TokenBlacklistService(IConnectionMultiplexer? redis = null)
        {
            _redis = redis;
        }

        public async Task BlacklistAsync(string tokenId, DateTimeOffset expiry)
        {
            if (_redis is null) return;

            var db = _redis.GetDatabase();
            var ttl = expiry - DateTimeOffset.UtcNow;
            if (ttl <= TimeSpan.Zero) return;

            await db.StringSetAsync($"blacklist:{tokenId}", "1", ttl);
        }

        public async Task<bool> IsBlacklistedAsync(string tokenId)
        {
            if (_redis is null) return false;

            var db = _redis.GetDatabase();
            return await db.KeyExistsAsync($"blacklist:{tokenId}");
        }
    }
}
