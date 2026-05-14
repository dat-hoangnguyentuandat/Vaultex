using App.Domain.Entities;
using App.Infrastructure.Data;
using Microsoft.AspNetCore.Http;

namespace App.Infrastructure.Services
{
    public class AuditLogService
    {
        private readonly AppDbContext _db;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuditLogService(AppDbContext db, IHttpContextAccessor httpContextAccessor)
        {
            _db = db;
            _httpContextAccessor = httpContextAccessor;
        }

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
                IpAddress    = http?.Connection.RemoteIpAddress?.ToString(),
                UserAgent    = http?.Request.Headers["User-Agent"].ToString(),
                Timestamp    = DateTime.UtcNow
            };

            _db.AuditLogs.Add(entry);
            await _db.SaveChangesAsync();
        }
    }
}
