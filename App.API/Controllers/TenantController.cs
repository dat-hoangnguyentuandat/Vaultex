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
        private readonly AppDbContext _db;
        private readonly AuditLogService _auditLog;
        private readonly RoleManager<AppRole> _roleManager;
        private readonly UserManager<User> _userManager;

        public TenantController(
            AppDbContext db,
            AuditLogService auditLog,
            RoleManager<AppRole> roleManager,
            UserManager<User> userManager)
        {
            _db = db;
            _auditLog = auditLog;
            _roleManager = roleManager;
            _userManager = userManager;
        }

        [HttpGet]
        public async Task<IActionResult> GetTenants(
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            var query = _db.Tenants.AsNoTracking().IgnoreQueryFilters();

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
            var userCounts = await _db.Users.IgnoreQueryFilters()
                .Where(u => u.TenantId.HasValue && tenantIds.Contains(u.TenantId!.Value))
                .GroupBy(u => u.TenantId!.Value)
                .Select(g => new { TenantId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.TenantId, x => x.Count);

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
            var tenant = await _db.Tenants.AsNoTracking().IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tenant is null) return NotFound();

            var userCount = await _db.Users.AsNoTracking().IgnoreQueryFilters()
                .CountAsync(u => u.TenantId == id);

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

            if (await _db.Tenants.IgnoreQueryFilters().AnyAsync(t => t.Subdomain == subdomain))
                return Conflict(new { error = "Subdomain already in use." });

            var tenant = new Tenant
            {
                Name = req.Name,
                Subdomain = subdomain,
                Region = req.Region ?? "",
                Status = TenantStatus.Active,
                Configuration = new TenantConfiguration()
            };

            _db.Tenants.Add(tenant);
            await _db.SaveChangesAsync();

            // Seed standard roles for the new tenant
            foreach (var roleName in Roles.All)
            {
                var fullName = $"{tenant.Id}:{roleName}";
                if (await _roleManager.FindByNameAsync(fullName) is null)
                {
                    await _roleManager.CreateAsync(new AppRole
                    {
                        Name = fullName,
                        NormalizedName = fullName.ToUpperInvariant(),
                        TenantId = tenant.Id
                    });
                }
            }

            await _auditLog.LogAsync(AuditEventTypes.TenantCreated,
                resourceType: "Tenant", resourceId: tenant.Id.ToString(), newValue: tenant.Name);

            return Ok(new { tenant.Id, tenant.Name, tenant.Subdomain });
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> UpdateTenant(Guid id, [FromBody] TenantUpdateRequest req)
        {
            var tenant = await _db.Tenants.IgnoreQueryFilters()
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
            var tenant = await _db.Tenants.IgnoreQueryFilters()
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
            var tenant = await _db.Tenants.IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == id);

            if (tenant is null) return NotFound();

            if (await _userManager.FindByEmailAsync(req.Email) is not null)
                return Conflict(new { error = "Email already in use." });

            var user = new User
            {
                UserName = req.Email,
                Email = req.Email,
                FullName = req.FullName,
                TenantId = tenant.Id,
                IsActive = true,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user, req.Password);
            if (!result.Succeeded)
                return BadRequest(result.Errors.Select(e => e.Description));

            await _userManager.AddToRoleAsync(user, $"{tenant.Id}:{Roles.Admin}");

            await _auditLog.LogAsync(AuditEventTypes.UserCreated,
                tenantId: tenant.Id, userId: user.Id,
                resourceType: "User", resourceId: user.Id.ToString());

            return Ok(new { userId = user.Id, user.Email, tenantId = tenant.Id });
        }

        [HttpGet("~/api/platform/stats")]
        public async Task<IActionResult> GetStats()
        {
            var tenantCount = await _db.Tenants.IgnoreQueryFilters().CountAsync();
            var activeTenants = await _db.Tenants.IgnoreQueryFilters()
                .CountAsync(t => t.Status == TenantStatus.Active);
            var userCount = await _db.Users.IgnoreQueryFilters().CountAsync();
            var today = DateTime.UtcNow.Date;
            var auditToday = await _db.AuditLogs
                .CountAsync(a => a.Timestamp >= today);

            return Ok(new { tenantCount, activeTenants, userCount, auditToday });
        }
    }

    public record TenantCreateRequest(string Name, string Subdomain, string? Region);
    public record TenantUpdateRequest(string Name, string? Region);
    public record TenantAdminRequest(string Email, string Password, string? FullName);
}
