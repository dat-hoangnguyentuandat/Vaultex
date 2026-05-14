using App.Domain.Entities;
using App.Infrastructure.Data;
using App.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;

namespace App.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
               Policy = "UserManage")]
    public class AuditController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly ITenantContext _tenantContext;

        public AuditController(AppDbContext db, ITenantContext tenantContext)
        {
            _db = db;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public async Task<IActionResult> GetLogs(
            [FromQuery] Guid? userId,
            [FromQuery] string? eventType,
            [FromQuery] DateTime? dateFrom,
            [FromQuery] DateTime? dateTo,
            [FromQuery] string? ipAddress,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            if (pageSize > 200) pageSize = 200;

            var query = _db.AuditLogs.AsNoTracking();

            // Scope to current tenant if resolved
            if (_tenantContext.TenantId.HasValue)
                query = query.Where(a => a.TenantId == _tenantContext.TenantId.Value);

            if (userId.HasValue)
                query = query.Where(a => a.UserId == userId.Value);

            if (!string.IsNullOrEmpty(eventType))
                query = query.Where(a => a.EventType == eventType);

            if (dateFrom.HasValue)
                query = query.Where(a => a.Timestamp >= dateFrom.Value.ToUniversalTime());

            if (dateTo.HasValue)
                query = query.Where(a => a.Timestamp <= dateTo.Value.ToUniversalTime());

            if (!string.IsNullOrEmpty(ipAddress))
                query = query.Where(a => a.IpAddress == ipAddress);

            var total = await query.CountAsync();

            var items = await query
                .OrderByDescending(a => a.Timestamp)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new
                {
                    a.Id,
                    a.TenantId,
                    a.UserId,
                    a.EventType,
                    a.ResourceType,
                    a.ResourceId,
                    a.IpAddress,
                    a.Timestamp
                })
                .ToListAsync();

            return Ok(new
            {
                total,
                page,
                pageSize,
                items
            });
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetLog(Guid id)
        {
            var query = _db.AuditLogs.AsNoTracking().Where(a => a.Id == id);

            if (_tenantContext.TenantId.HasValue)
                query = query.Where(a => a.TenantId == _tenantContext.TenantId.Value);

            var log = await query.FirstOrDefaultAsync();
            if (log is null) return NotFound();

            return Ok(log);
        }
    }
}
