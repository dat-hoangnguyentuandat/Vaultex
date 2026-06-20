using App.Domain.Entities;
using App.Infrastructure.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Services
{
    public class AuditLogService
    {
        private readonly TenantDbContext _db;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<AuditLogService> _logger;

        public AuditLogService(TenantDbContext db, IHttpContextAccessor httpContextAccessor, ILogger<AuditLogService> logger)
        {
            _db = db;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        private static string? NormalizeIp(string? ip) =>
            ip is "::1" or "::ffff:127.0.0.1" ? "127.0.0.1" : ip;

        public async Task LogAsync(
            string eventType,
            Guid? tenantId = null,
            Guid? userId = null,
            string? resourceType = null,
            string? resourceId = null,
            string? oldValue = null,
            string? newValue = null)
        {
            var http = _httpContextAccessor.HttpContext;

            var entry = new AuditLog
            {
                EventType    = eventType,
                TenantId     = tenantId,
                UserId       = userId,
                ResourceType = resourceType,
                ResourceId   = resourceId,
                OldValue     = oldValue,
                NewValue     = newValue,
                IpAddress    = NormalizeIp(http?.Connection.RemoteIpAddress?.ToString()),
                UserAgent    = http?.Request.Headers["User-Agent"].ToString(),
                Timestamp    = DateTime.UtcNow
            };

            _db.AuditLogs.Add(entry);

            try
            {
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist audit log entry. EventType={EventType} TenantId={TenantId} UserId={UserId}",
                    eventType, tenantId, userId);
            }
        }
    }
}
