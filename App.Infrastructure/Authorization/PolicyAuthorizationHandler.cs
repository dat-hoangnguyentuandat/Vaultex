using App.Infrastructure.Data;
using App.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;

namespace App.Infrastructure.Authorization
{
    public class PolicyRequirement : IAuthorizationRequirement
    {
        public string Resource { get; }
        public string Action { get; }

        public PolicyRequirement(string resource, string action)
        {
            Resource = resource;
            Action = action;
        }
    }

    public class PolicyAuthorizationHandler : AuthorizationHandler<PolicyRequirement>
    {
        private readonly AppDbContext _db;
        private readonly ITenantContext _tenantContext;
        private readonly PolicyEngine _engine;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public PolicyAuthorizationHandler(
            AppDbContext db,
            ITenantContext tenantContext,
            PolicyEngine engine,
            IHttpContextAccessor httpContextAccessor)
        {
            _db = db;
            _tenantContext = tenantContext;
            _engine = engine;
            _httpContextAccessor = httpContextAccessor;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PolicyRequirement requirement)
        {
            if (!context.User.Identity?.IsAuthenticated ?? true)
                return;

            var userIdStr = context.User.GetClaim(OpenIddictConstants.Claims.Subject);
            if (!Guid.TryParse(userIdStr, out var userId))
                return;

            var tenantId = _tenantContext.TenantId;
            if (!tenantId.HasValue)
            {
                context.Succeed(requirement);
                return;
            }

            var policies = await _db.Policies
                .AsNoTracking()
                .Where(p => p.TenantId == tenantId.Value
                         && p.Resource == requirement.Resource
                         && p.Action == requirement.Action
                         && p.IsActive)
                .ToListAsync();

            // No policies defined for this resource+action → allow (RBAC already checked)
            if (policies.Count == 0)
            {
                context.Succeed(requirement);
                return;
            }

            var ip = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

            var evalCtx = new PolicyEvaluationContext
            {
                UserId = userId,
                TenantId = tenantId,
                IpAddress = ip,
                Resource = requirement.Resource,
                Action = requirement.Action,
                RequestTime = DateTime.UtcNow
            };

            // Resource owner context can be set via HttpContext.Items by the controller
            if (_httpContextAccessor.HttpContext?.Items.TryGetValue("ResourceOwnerId", out var ownerObj) == true
                && ownerObj is Guid ownerId)
            {
                evalCtx.ResourceOwnerId = ownerId;
            }

            if (_httpContextAccessor.HttpContext?.Items.TryGetValue("ResourceTenantId", out var resTenantObj) == true
                && resTenantObj is Guid resTenantId)
            {
                evalCtx.ResourceTenantId = resTenantId;
            }

            // At least one policy must pass
            if (policies.Any(p => _engine.Evaluate(p, evalCtx)))
                context.Succeed(requirement);
        }
    }
}
