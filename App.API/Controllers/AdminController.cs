using App.Domain.Constants;
using App.Domain.Entities;
using App.Infrastructure.Data;
using App.Infrastructure.Services;
using App.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;

namespace App.API.Controllers
{
    [ApiController]
    [Route("api/admin")]
    [Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
               Policy = "UserManage")]
    public class AdminController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly UserManager<User> _userManager;
        private readonly RoleManager<AppRole> _roleManager;
        private readonly ITenantContext _tenantContext;
        private readonly AuditLogService _auditLog;

        public AdminController(
            AppDbContext db,
            UserManager<User> userManager,
            RoleManager<AppRole> roleManager,
            ITenantContext tenantContext,
            AuditLogService auditLog)
        {
            _db = db;
            _userManager = userManager;
            _roleManager = roleManager;
            _tenantContext = tenantContext;
            _auditLog = auditLog;
        }

        // ── Users ──────────────────────────────────────────────────────────

        [HttpGet("users")]
        public async Task<IActionResult> GetUsers(
            [FromQuery] string? search,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            if (!_tenantContext.TenantId.HasValue) return BadRequest("Tenant not resolved.");

            var query = _db.Users.AsNoTracking()
                .Where(u => u.TenantId == _tenantContext.TenantId.Value);

            if (!string.IsNullOrEmpty(search))
                query = query.Where(u => u.Email!.Contains(search) || (u.FullName != null && u.FullName.Contains(search)));

            var total = await query.CountAsync();
            var users = await query
                .OrderBy(u => u.Email)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(u => new { u.Id, u.Email, u.FullName, u.IsActive, u.CreatedAt, u.LastLoginAt })
                .ToListAsync();

            return Ok(new { total, page, pageSize, items = users });
        }

        [HttpGet("users/{id:guid}")]
        public async Task<IActionResult> GetUser(Guid id)
        {
            if (!_tenantContext.TenantId.HasValue) return BadRequest("Tenant not resolved.");

            var user = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == _tenantContext.TenantId.Value);

            if (user is null) return NotFound();

            var roles = await _userManager.GetRolesAsync(user);
            var shortRoles = roles.Select(r => r.Contains(':') ? r.Split(':', 2)[1] : r).ToList();

            return Ok(new
            {
                user.Id, user.Email, user.FullName, user.PhoneNumber,
                user.Company, user.Position, user.IsActive,
                user.CreatedAt, user.LastLoginAt,
                roles = shortRoles
            });
        }

        [HttpPost("users")]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest req)
        {
            if (!_tenantContext.TenantId.HasValue) return BadRequest("Tenant not resolved.");

            var user = new User
            {
                UserName = req.Email,
                Email = req.Email,
                FullName = req.FullName,
                TenantId = _tenantContext.TenantId.Value,
                IsActive = true,
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user, req.Password);
            if (!result.Succeeded)
                return BadRequest(result.Errors.Select(e => e.Description));

            if (!string.IsNullOrEmpty(req.Role))
            {
                var roleName = $"{_tenantContext.TenantId.Value}:{req.Role}";
                await _userManager.AddToRoleAsync(user, roleName);
            }

            await _auditLog.LogAsync(AuditEventTypes.UserCreated, _tenantContext.TenantId, user.Id,
                resourceType: "User", resourceId: user.Id.ToString());

            return Ok(new { user.Id, user.Email });
        }

        [HttpPut("users/{id:guid}/activate")]
        public async Task<IActionResult> SetActive(Guid id, [FromQuery] bool active)
        {
            if (!_tenantContext.TenantId.HasValue) return BadRequest("Tenant not resolved.");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == _tenantContext.TenantId.Value);
            if (user is null) return NotFound();

            user.IsActive = active;
            await _db.SaveChangesAsync();

            return Ok(new { user.Id, user.IsActive });
        }

        [HttpPut("users/{id:guid}/roles")]
        public async Task<IActionResult> SetRoles(Guid id, [FromBody] SetRolesRequest req)
        {
            if (!_tenantContext.TenantId.HasValue) return BadRequest("Tenant not resolved.");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == _tenantContext.TenantId.Value);
            if (user is null) return NotFound();

            var currentRoles = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, currentRoles);

            var newRoles = req.Roles.Select(r => $"{_tenantContext.TenantId.Value}:{r}").ToList();
            await _userManager.AddToRolesAsync(user, newRoles);

            await _auditLog.LogAsync(AuditEventTypes.RoleChanged, _tenantContext.TenantId, user.Id,
                resourceType: "User", resourceId: user.Id.ToString(),
                oldValue: string.Join(",", currentRoles),
                newValue: string.Join(",", newRoles));

            return Ok();
        }

        [HttpPost("users/{id:guid}/reset-password")]
        public async Task<IActionResult> ResetPassword(Guid id, [FromBody] ResetPasswordRequest req)
        {
            if (!_tenantContext.TenantId.HasValue) return BadRequest("Tenant not resolved.");

            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == _tenantContext.TenantId.Value);
            if (user is null) return NotFound();

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var result = await _userManager.ResetPasswordAsync(user, token, req.NewPassword);

            if (!result.Succeeded)
                return BadRequest(result.Errors.Select(e => e.Description));

            await _auditLog.LogAsync(AuditEventTypes.PasswordReset, _tenantContext.TenantId, user.Id,
                resourceType: "User", resourceId: user.Id.ToString());

            return Ok();
        }

        // ── Roles ──────────────────────────────────────────────────────────

        [HttpGet("roles")]
        public async Task<IActionResult> GetRoles()
        {
            if (!_tenantContext.TenantId.HasValue) return BadRequest("Tenant not resolved.");

            var roles = await _db.Set<AppRole>().AsNoTracking()
                .Where(r => r.TenantId == _tenantContext.TenantId.Value)
                .Select(r => new { r.Id, r.Name })
                .ToListAsync();

            var result = roles.Select(r => new
            {
                r.Id,
                ShortName = r.Name != null && r.Name.Contains(':') ? r.Name.Split(':')[1] : r.Name
            });

            return Ok(result);
        }

        [HttpPost("roles")]
        public async Task<IActionResult> CreateRole([FromBody] CreateRoleRequest req)
        {
            if (!_tenantContext.TenantId.HasValue) return BadRequest("Tenant not resolved.");

            var roleName = $"{_tenantContext.TenantId.Value}:{req.Name}";
            var role = new AppRole { Name = roleName, TenantId = _tenantContext.TenantId.Value };
            var result = await _roleManager.CreateAsync(role);

            if (!result.Succeeded)
                return BadRequest(result.Errors.Select(e => e.Description));

            return Ok(new { role.Id, req.Name });
        }

        [HttpDelete("roles/{id:guid}")]
        public async Task<IActionResult> DeleteRole(Guid id)
        {
            if (!_tenantContext.TenantId.HasValue) return BadRequest("Tenant not resolved.");

            var role = await _db.Set<AppRole>()
                .FirstOrDefaultAsync(r => r.Id == id && r.TenantId == _tenantContext.TenantId.Value);

            if (role is null) return NotFound();

            await _roleManager.DeleteAsync(role);
            return NoContent();
        }

        // ── Policies ───────────────────────────────────────────────────────

        [HttpGet("policies")]
        public async Task<IActionResult> GetPolicies()
        {
            if (!_tenantContext.TenantId.HasValue) return BadRequest("Tenant not resolved.");

            var policies = await _db.Policies.AsNoTracking()
                .Where(p => p.TenantId == _tenantContext.TenantId.Value)
                .Select(p => new { p.Id, p.Name, p.Resource, p.Action, p.IsActive, p.CreatedAt })
                .ToListAsync();

            return Ok(policies);
        }

        [HttpGet("policies/{id:guid}")]
        public async Task<IActionResult> GetPolicy(Guid id)
        {
            if (!_tenantContext.TenantId.HasValue) return BadRequest("Tenant not resolved.");

            var policy = await _db.Policies.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _tenantContext.TenantId.Value);

            if (policy is null) return NotFound();
            return Ok(policy);
        }

        [HttpPost("policies")]
        public async Task<IActionResult> CreatePolicy([FromBody] PolicyUpsertRequest req)
        {
            if (!_tenantContext.TenantId.HasValue) return BadRequest("Tenant not resolved.");

            var policy = new Policy
            {
                TenantId = _tenantContext.TenantId.Value,
                Name = req.Name,
                Resource = req.Resource,
                Action = req.Action,
                ConditionsJson = req.ConditionsJson ?? "[]",
                IsActive = true
            };

            _db.Policies.Add(policy);
            await _db.SaveChangesAsync();

            await _auditLog.LogAsync(AuditEventTypes.PolicyChanged, _tenantContext.TenantId,
                resourceType: "Policy", resourceId: policy.Id.ToString(), newValue: req.ConditionsJson);

            return Ok(new { policy.Id });
        }

        [HttpPut("policies/{id:guid}")]
        public async Task<IActionResult> UpdatePolicy(Guid id, [FromBody] PolicyUpsertRequest req)
        {
            if (!_tenantContext.TenantId.HasValue) return BadRequest("Tenant not resolved.");

            var policy = await _db.Policies
                .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _tenantContext.TenantId.Value);

            if (policy is null) return NotFound();

            var old = policy.ConditionsJson;
            policy.Name = req.Name;
            policy.Resource = req.Resource;
            policy.Action = req.Action;
            policy.ConditionsJson = req.ConditionsJson ?? "[]";
            policy.IsActive = req.IsActive;

            await _db.SaveChangesAsync();

            await _auditLog.LogAsync(AuditEventTypes.PolicyChanged, _tenantContext.TenantId,
                resourceType: "Policy", resourceId: policy.Id.ToString(),
                oldValue: old, newValue: policy.ConditionsJson);

            return Ok();
        }

        [HttpDelete("policies/{id:guid}")]
        public async Task<IActionResult> DeletePolicy(Guid id)
        {
            if (!_tenantContext.TenantId.HasValue) return BadRequest("Tenant not resolved.");

            var policy = await _db.Policies
                .FirstOrDefaultAsync(p => p.Id == id && p.TenantId == _tenantContext.TenantId.Value);

            if (policy is null) return NotFound();

            _db.Policies.Remove(policy);
            await _db.SaveChangesAsync();
            return NoContent();
        }
    }

    // ── Request DTOs ───────────────────────────────────────────────────────

    public record CreateUserRequest(string Email, string Password, string? FullName, string? Role);
    public record SetRolesRequest(List<string> Roles);
    public record ResetPasswordRequest(string NewPassword);
    public record CreateRoleRequest(string Name);
    public record PolicyUpsertRequest(string Name, string Resource, string Action, string? ConditionsJson, bool IsActive = true);
}
