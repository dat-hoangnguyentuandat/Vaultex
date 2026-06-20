using App.Domain.Constants;
using App.Domain.Entities;
using App.Infrastructure.Data;
using App.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;

namespace App.API.Controllers
{
    [ApiController]
    [Route("api/platform/tenants")]
    [Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
               Policy = "PlatformAdmin")]
    public class TenantController : ControllerBase
    {
        private readonly PlatformDbContext _db;
        private readonly AuditLogService _auditLog;
        private readonly TenantProvisioningService _provisioning;

        public TenantController(
            PlatformDbContext db,
            AuditLogService auditLog,
            TenantProvisioningService provisioning)
        {
            _db = db;
            _auditLog = auditLog;
            _provisioning = provisioning;
        }

        [HttpGet]
        public async Task<IActionResult> GetTenants(
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var query = _db.Tenants.AsNoTracking();

            if (!string.IsNullOrEmpty(search))
                query = query.Where(t => t.Name.Contains(search) || t.Subdomain.Contains(search));

            var total = await query.CountAsync();
            var tenants = await query
                .OrderBy(t => t.Name)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new { t.Id, t.Name, t.Subdomain, t.Region, t.Status, t.CreatedAt })
                .ToListAsync();

            var tenantIds = tenants.Select(t => t.Id).ToList();
            var userCounts = new Dictionary<Guid, int>();
            foreach (var tenant in await _db.Tenants.AsNoTracking()
                         .Where(t => tenantIds.Contains(t.Id))
                         .ToListAsync())
            {
                userCounts[tenant.Id] = await _provisioning.CountTenantUsersAsync(tenant);
            }

            var items = tenants.Select(t => new
            {
                t.Id, t.Name, t.Subdomain, t.Region, t.Status, t.CreatedAt,
                UserCount = userCounts.GetValueOrDefault(t.Id, 0)
            }).ToList();

            return Ok(new { total, page, pageSize, items });
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetTenant(Guid id)
        {
            var tenant = await _db.Tenants.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tenant is null) return NotFound();

            var userCount = await _provisioning.CountTenantUsersAsync(tenant);

            return Ok(new
            {
                tenant.Id, tenant.Name, tenant.Subdomain,
                tenant.Region, tenant.Status, tenant.CreatedAt,
                userCount
            });
        }

        [HttpPost]
        public async Task<IActionResult> CreateTenant([FromBody] TenantCreateRequest req)
        {
            var subdomain = req.Subdomain.ToLowerInvariant();

            if (await _db.Tenants.AnyAsync(t => t.Subdomain == subdomain))
                return Conflict(new { error = "Subdomain already in use." });

            var tenant = await _provisioning.CreateTenantAsync(req.Name, subdomain, req.Region);

            await _auditLog.LogAsync(AuditEventTypes.TenantCreated,
                resourceType: "Tenant", resourceId: tenant.Id.ToString(), newValue: tenant.Name);

            return Ok(new { tenant.Id, tenant.Name, tenant.Subdomain });
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> UpdateTenant(Guid id, [FromBody] TenantUpdateRequest req)
        {
            var tenant = await _db.Tenants
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tenant is null) return NotFound();

            var oldName = tenant.Name;
            tenant.Name = req.Name;
            tenant.Region = req.Region ?? tenant.Region;

            await _db.SaveChangesAsync();

            await _auditLog.LogAsync(AuditEventTypes.TenantUpdated,
                resourceType: "Tenant", resourceId: tenant.Id.ToString(),
                oldValue: oldName, newValue: tenant.Name);

            return Ok(new { tenant.Id, tenant.Name, tenant.Subdomain, tenant.Region });
        }

        [HttpPut("{id:guid}/status")]
        public async Task<IActionResult> SetStatus(Guid id, [FromQuery] TenantStatus status)
        {
            var tenant = await _db.Tenants
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tenant is null) return NotFound();

            var oldStatus = tenant.Status.ToString();
            tenant.Status = status;
            await _db.SaveChangesAsync();

            await _auditLog.LogAsync(AuditEventTypes.TenantStatusChanged,
                resourceType: "Tenant", resourceId: tenant.Id.ToString(),
                oldValue: oldStatus, newValue: status.ToString());

            return Ok(new { tenant.Id, tenant.Status });
        }

        [HttpPost("{id:guid}/admin")]
        public async Task<IActionResult> CreateTenantAdmin(Guid id, [FromBody] TenantAdminRequest req)
        {
            var tenant = await _db.Tenants
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tenant is null) return NotFound();

            User user;
            try
            {
                user = await _provisioning.CreateTenantAdminAsync(tenant.Id, req.Email, req.Password, req.FullName);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Email already", StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }

            await _auditLog.LogAsync(AuditEventTypes.UserCreated,
                tenantId: tenant.Id, userId: user.Id,
                resourceType: "User", resourceId: user.Id.ToString());

            return Ok(new { userId = user.Id, user.Email, tenantId = tenant.Id });
        }

        [HttpGet("~/api/platform/stats")]
        public async Task<IActionResult> GetStats()
        {
            var tenantCount = await _db.Tenants.CountAsync();
            var activeTenants = await _db.Tenants
                .CountAsync(t => t.Status == TenantStatus.Active);
            var userCount = 0;
            foreach (var tenant in await _db.Tenants.AsNoTracking().ToListAsync())
                userCount += await _provisioning.CountTenantUsersAsync(tenant);
            var auditToday = 0;

            return Ok(new { tenantCount, activeTenants, userCount, auditToday });
        }
    }

    public record TenantCreateRequest(string Name, string Subdomain, string? Region);
    public record TenantUpdateRequest(string Name, string? Region);
    public record TenantAdminRequest(string Email, string Password, string? FullName);
}
