using App.Domain.Entities;
using App.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace App.API.Controllers;

/// <summary>
/// Lets the signed-in user view and revoke OAuth authorizations they have granted to apps.
/// Each "authorization" represents a permanent consent ("App X may access your email/profile").
/// Revoking one removes the consent and revokes all tokens issued under it.
/// </summary>
[ApiController]
[Route("api/account/authorizations")]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
public class AccountAuthorizationsController : ControllerBase
{
    private readonly IOpenIddictAuthorizationManager _authorizationManager;
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictTokenManager _tokenManager;
    private readonly AuditLogService _auditLog;

    public AccountAuthorizationsController(
        IOpenIddictAuthorizationManager authorizationManager,
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictTokenManager tokenManager,
        AuditLogService auditLog)
    {
        _authorizationManager = authorizationManager;
        _applicationManager = applicationManager;
        _tokenManager = tokenManager;
        _auditLog = auditLog;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var userId = User.GetClaim(Claims.Subject);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var items = new List<object>();
        await foreach (var auth in _authorizationManager.FindBySubjectAsync(userId))
        {
            var status = await _authorizationManager.GetStatusAsync(auth);
            if (status != Statuses.Valid) continue;

            var clientId = await _authorizationManager.GetApplicationIdAsync(auth);
            if (string.IsNullOrEmpty(clientId)) continue;

            var app = await _applicationManager.FindByIdAsync(clientId);
            if (app is null) continue;

            items.Add(new
            {
                id = await _authorizationManager.GetIdAsync(auth),
                clientId = await _applicationManager.GetClientIdAsync(app),
                displayName = await _applicationManager.GetDisplayNameAsync(app),
                scopes = await _authorizationManager.GetScopesAsync(auth),
                grantedAt = await _authorizationManager.GetCreationDateAsync(auth),
            });
        }

        return Ok(items);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Revoke(string id)
    {
        var userId = User.GetClaim(Claims.Subject);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();

        var auth = await _authorizationManager.FindByIdAsync(id);
        if (auth is null) return NotFound();

        // Ownership check: a user may only revoke their own authorizations.
        var subject = await _authorizationManager.GetSubjectAsync(auth);
        if (subject != userId) return NotFound();

        var clientDbId = await _authorizationManager.GetApplicationIdAsync(auth);
        var clientId = clientDbId is not null
            ? await _applicationManager.GetClientIdAsync(
                await _applicationManager.FindByIdAsync(clientDbId) ?? throw new InvalidOperationException())
            : null;

        // Revoke all tokens issued under this authorization, then revoke the authorization itself.
        await foreach (var token in _tokenManager.FindByAuthorizationIdAsync(id))
            await _tokenManager.TryRevokeAsync(token);

        await _authorizationManager.TryRevokeAsync(auth);

        Guid? tenantId = Guid.TryParse(User.GetClaim("tenant_id"), out var t) ? t : null;
        Guid? userGuid = Guid.TryParse(userId, out var u) ? u : null;

        await _auditLog.LogAsync(AuditEventTypes.AuthorizationRevoked, tenantId, userGuid,
            resourceType: "OidcAuthorization", resourceId: id, oldValue: clientId);

        return NoContent();
    }
}
