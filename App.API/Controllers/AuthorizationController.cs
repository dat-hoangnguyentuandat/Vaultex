using System.Security.Claims;
using App.Application.Interfaces;
using App.Domain.Entities;
using App.Domain.Constants;
using App.Infrastructure.Services;
using App.Infrastructure.Tenancy;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using System.Collections.Immutable;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace App.API.Controllers;

public class AuthorizationController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly SignInManager<User> _signInManager;
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictAuthorizationManager _authorizationManager;
    private readonly IOpenIddictScopeManager _scopeManager;
    private readonly IOpenIddictTokenManager _tokenManager;
    private readonly ILogger<AuthorizationController> _logger;
    private readonly AuditLogService _auditLog;
    private readonly ITenantContext _tenantContext;
    private readonly TokenBlacklistService _blacklist;

    public AuthorizationController(
        UserManager<User> userManager,
        SignInManager<User> signInManager,
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictAuthorizationManager authorizationManager,
        IOpenIddictScopeManager scopeManager,
        IOpenIddictTokenManager tokenManager,
        ILogger<AuthorizationController> logger,
        AuditLogService auditLog,
        ITenantContext tenantContext,
        TokenBlacklistService blacklist)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _applicationManager = applicationManager;
        _authorizationManager = authorizationManager;
        _scopeManager = scopeManager;
        _tokenManager = tokenManager;
        _logger = logger;
        _auditLog = auditLog;
        _tenantContext = tenantContext;
        _blacklist = blacklist;
    }

    private async Task<ClaimsIdentity> BuildIdentityAsync(User user)
    {
        var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        var subClaim = new Claim(OpenIddictConstants.Claims.Subject, user.Id.ToString());
        subClaim.SetDestinations(OpenIddictConstants.Destinations.AccessToken,
                                 OpenIddictConstants.Destinations.IdentityToken);
        identity.AddClaim(subClaim);

        var emailClaim = new Claim(OpenIddictConstants.Claims.Email, user.Email!);
        emailClaim.SetDestinations(OpenIddictConstants.Destinations.AccessToken,
                                   OpenIddictConstants.Destinations.IdentityToken);
        identity.AddClaim(emailClaim);

        var nameClaim = new Claim(OpenIddictConstants.Claims.Name, user.FullName ?? user.Email!);
        nameClaim.SetDestinations(OpenIddictConstants.Destinations.AccessToken,
                                  OpenIddictConstants.Destinations.IdentityToken);
        identity.AddClaim(nameClaim);

        // Tenant claim
        if (user.TenantId.HasValue)
        {
            var tenantClaim = new Claim("tenant_id", user.TenantId.Value.ToString());
            tenantClaim.SetDestinations(OpenIddictConstants.Destinations.AccessToken,
                                        OpenIddictConstants.Destinations.IdentityToken);
            identity.AddClaim(tenantClaim);
        }

        // Roles — stored as "{tenantId}:RoleName", expose only the short name in token
        var fullRoleNames = await _userManager.GetRolesAsync(user);
        var shortRoleNames = fullRoleNames
            .Select(r => r.Contains(':') ? r.Split(':', 2)[1] : r)
            .ToList();

        foreach (var role in shortRoleNames)
        {
            var roleClaim = new Claim(OpenIddictConstants.Claims.Role, role);
            roleClaim.SetDestinations(OpenIddictConstants.Destinations.AccessToken,
                                       OpenIddictConstants.Destinations.IdentityToken);
            identity.AddClaim(roleClaim);
        }

        // Permissions derived from short role names
        var permissions = new HashSet<string>();
        foreach (var role in shortRoleNames)
        {
            if (RolePermissions.Mapping.TryGetValue(role, out var rolePermissions))
            {
                foreach (var permission in rolePermissions)
                    permissions.Add(permission);
            }
        }

        foreach (var permission in permissions)
        {
            var permissionClaim = new Claim("permission", permission);
            permissionClaim.SetDestinations(OpenIddictConstants.Destinations.AccessToken,
                                            OpenIddictConstants.Destinations.IdentityToken);
            identity.AddClaim(permissionClaim);
        }

        return identity;
    }

    // Được gọi sau khi Login.cshtml.cs xác thực thành công và redirect về đây.
    // Tạo authorization_code (mã ủy quyền tạm thời, sống 5 phút, dùng 1 lần)
    // rồi redirect về App.Web kèm code trong URL:
    //   https://localhost:7066/signin-oidc?code=<authorization_code>
    // App.Web sẽ nhận code này và gọi Exchange() bên dưới để đổi lấy token thật.
    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var result = await HttpContext.AuthenticateAsync();
        if (result is not { Succeeded: true })
        {
            return Challenge(new AuthenticationProperties
            {
                RedirectUri = Request.PathBase + Request.Path + QueryString.Create(
                    Request.HasFormContentType ? Request.Form : Request.Query)
            });
        }

        var user = await _userManager.GetUserAsync(result.Principal)
            ?? throw new InvalidOperationException("The user details cannot be retrieved.");

        var application = await _applicationManager.FindByClientIdAsync(request.ClientId!)
            ?? throw new InvalidOperationException("The application cannot be found.");

        var authorizationList = new List<object>();
        await foreach (var item in _authorizationManager.FindAsync(
            subject: await _userManager.GetUserIdAsync(user),
            client: await _applicationManager.GetIdAsync(application),
            status: Statuses.Valid,
            type: AuthorizationTypes.Permanent,
            scopes: request.GetScopes()))
        {
            authorizationList.Add(item);
        }

        var identity = await BuildIdentityAsync(user);

        identity.SetClaim(Claims.Subject, await _userManager.GetUserIdAsync(user))
                .SetClaim(Claims.Email, await _userManager.GetEmailAsync(user))
                .SetClaim(Claims.Name, await _userManager.GetUserNameAsync(user));

        identity.SetScopes(request.GetScopes());
        var resources = new List<string>();
        await foreach (var resource in _scopeManager.ListResourcesAsync(identity.GetScopes()))
            resources.Add(resource);
        identity.SetResources(resources);

        var authorization = authorizationList.LastOrDefault();
        authorization ??= await _authorizationManager.CreateAsync(
            identity: identity,
            subject: await _userManager.GetUserIdAsync(user),
            client: (await _applicationManager.GetIdAsync(application))!,
            type: AuthorizationTypes.Permanent,
            scopes: identity.GetScopes());

        identity.SetAuthorizationId(await _authorizationManager.GetIdAsync(authorization));
        identity.SetDestinations(GetDestinations);

        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpPost("~/connect/token")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("TokenEndpoint")]
    public async Task<IActionResult> Exchange()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OpenIddict request không hợp lệ");

        if (request.IsAuthorizationCodeGrantType())
        {
            var authResult = await HttpContext.AuthenticateAsync(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var principal = authResult.Principal!;

            principal.SetDestinations(GetDestinations);

            var userId = principal.GetClaim(OpenIddictConstants.Claims.Subject);
            var user = userId is not null ? await _userManager.FindByIdAsync(userId) : null;
            if (user is not null)
            {
                user.LastLoginAt = DateTime.UtcNow;
                await _userManager.UpdateAsync(user);
                await _auditLog.LogAsync(AuditEventTypes.LoginSuccess, user.TenantId, user.Id);
            }

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsPasswordGrantType())
        {
            var user = await _userManager.FindByEmailAsync(request.Username!);

            if (user == null || !user.IsActive)
            {
                await _auditLog.LogAsync(AuditEventTypes.LoginFailed, _tenantContext.TenantId);
                return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            var checkResult = await _signInManager.CheckPasswordSignInAsync(user, request.Password!, lockoutOnFailure: true);

            if (!checkResult.Succeeded)
            {
                await _auditLog.LogAsync(AuditEventTypes.LoginFailed, user.TenantId, user.Id);
                return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            }

            user.LastLoginAt = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);
            await _auditLog.LogAsync(AuditEventTypes.LoginSuccess, user.TenantId, user.Id);

            var identity = await BuildIdentityAsync(user);
            var principal = new ClaimsPrincipal(identity);
            principal.SetScopes(request.GetScopes());

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsRefreshTokenGrantType())
        {
            var authResult = await HttpContext.AuthenticateAsync(
                OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var principal = authResult.Principal!;

            // Rebuild identity from DB so revoked/deactivated users are rejected immediately
            var userId = principal.GetClaim(OpenIddictConstants.Claims.Subject);
            var user = userId is not null ? await _userManager.FindByIdAsync(userId) : null;

            if (user is null || !user.IsActive)
                return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

            var identity = await BuildIdentityAsync(user);
            var newPrincipal = new ClaimsPrincipal(identity);
            newPrincipal.SetScopes(principal.GetScopes());
            newPrincipal.SetDestinations(GetDestinations);

            return SignIn(newPrincipal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        if (request.IsClientCredentialsGrantType())
        {
            var application = await _applicationManager.FindByClientIdAsync(request.ClientId!)
                ?? throw new InvalidOperationException("The application cannot be found.");

            var identity = new ClaimsIdentity(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            var subClaim = new Claim(Claims.Subject, (await _applicationManager.GetClientIdAsync(application))!);
            subClaim.SetDestinations(Destinations.AccessToken);
            identity.AddClaim(subClaim);

            var nameClaim = new Claim(Claims.Name, (await _applicationManager.GetDisplayNameAsync(application))!);
            nameClaim.SetDestinations(Destinations.AccessToken);
            identity.AddClaim(nameClaim);

            var principal = new ClaimsPrincipal(identity);
            principal.SetScopes(request.GetScopes());

            return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        }

        return Forbid(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    [HttpGet("~/connect/userinfo")]
    [HttpPost("~/connect/userinfo")]
    [Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
    public async Task<IActionResult> Userinfo()
    {
        var userId = User.GetClaim(Claims.Subject);
        var user = userId is not null ? await _userManager.FindByIdAsync(userId) : null;

        if (user is null)
            return Challenge(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        var claims = new Dictionary<string, object>
        {
            [Claims.Subject] = user.Id.ToString()
        };

        if (User.HasScope(Scopes.Email))
        {
            claims[Claims.Email] = user.Email!;
            claims[Claims.EmailVerified] = user.EmailConfirmed;
        }

        if (User.HasScope(Scopes.Profile))
        {
            claims[Claims.Name] = user.FullName ?? user.Email!;
            if (user.DateOfBirth.HasValue)
                claims[Claims.Birthdate] = user.DateOfBirth.Value.ToString("yyyy-MM-dd");
        }

        if (User.HasScope(Scopes.Roles))
        {
            var fullRoles = await _userManager.GetRolesAsync(user);
            claims[Claims.Role] = fullRoles
                .Select(r => r.Contains(':') ? r.Split(':', 2)[1] : r)
                .ToArray();
        }

        if (user.TenantId.HasValue)
            claims["tenant_id"] = user.TenantId.Value.ToString();

        return Ok(claims);
    }

    [HttpGet("~/connect/logout")]
    [HttpPost("~/connect/logout")]
    public async Task<IActionResult> Logout()
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OpenIddict request không hợp lệ");

        var userId = _userManager.GetUserId(User);
        var username = _userManager.GetUserName(User) ?? userId ?? "unknown";

        if (!string.IsNullOrEmpty(userId))
        {
            // Blacklist the current access token immediately (fast-path for token validation)
            var jti = User.GetClaim(OpenIddictConstants.Claims.JwtId);
            var expClaim = User.FindFirst("exp")?.Value;
            if (!string.IsNullOrEmpty(jti) && long.TryParse(expClaim, out var expUnix))
                await _blacklist.BlacklistAsync(jti, DateTimeOffset.FromUnixTimeSeconds(expUnix));

            await foreach (var token in _tokenManager.FindBySubjectAsync(userId))
                await _tokenManager.TryRevokeAsync(token);

            if (Guid.TryParse(userId, out var userGuid))
            {
                var user = await _userManager.FindByIdAsync(userId);
                await _auditLog.LogAsync(AuditEventTypes.Logout, user?.TenantId, userGuid);
            }
        }

        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);

        _logger.LogInformation(
            "[AUDIT] LOGOUT | UserId={UserId} | Username={Username}",
            userId ?? "unknown", username);

        return SignOut(
            authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            properties: new AuthenticationProperties
            {
                RedirectUri = request.PostLogoutRedirectUri ?? "/"
            });
    }

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        return claim.Type switch
        {
            Claims.Name when claim.Subject!.HasScope(Scopes.Profile)
                => [Destinations.AccessToken, Destinations.IdentityToken],

            Claims.Email when claim.Subject!.HasScope(Scopes.Email)
                => [Destinations.AccessToken, Destinations.IdentityToken],

            Claims.Role when claim.Subject!.HasScope(Scopes.Roles)
                => [Destinations.AccessToken, Destinations.IdentityToken],

            "permission" when claim.Subject!.HasScope(Scopes.Roles)
                => [Destinations.AccessToken, Destinations.IdentityToken],

            _ => [Destinations.AccessToken]
        };
    }
}
