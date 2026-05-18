using App.Domain.Entities;
using App.Infrastructure.Services;
using App.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using System.Text.Json;

namespace App.API.Controllers;

[ApiController]
[Route("api/admin/clients")]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme,
           Policy = "UserManage")]
public class OidcClientController : ControllerBase
{
    private readonly IOpenIddictApplicationManager _appManager;
    private readonly ITenantContext _tenantContext;
    private readonly AuditLogService _auditLog;

    // Property key used to tag each application with its owning tenant.
    private const string TenantIdProperty = "tenant_id";

    public OidcClientController(
        IOpenIddictApplicationManager appManager,
        ITenantContext tenantContext,
        AuditLogService auditLog)
    {
        _appManager = appManager;
        _tenantContext = tenantContext;
        _auditLog = auditLog;
    }

    // ── List ──────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (!_tenantContext.TenantId.HasValue) return BadRequest("Tenant not resolved.");

        var tenantId = _tenantContext.TenantId.Value.ToString();
        var all = new List<object>();

        await foreach (var app in _appManager.ListAsync())
        {
            var props = await _appManager.GetPropertiesAsync(app);
            if (props.TryGetValue(TenantIdProperty, out var tid) && tid.GetString() == tenantId)
                all.Add(app);
        }

        var total = all.Count;
        var items = new List<object>();
        foreach (var app in all.Skip((page - 1) * pageSize).Take(pageSize))
        {
            var descriptor = new OpenIddictApplicationDescriptor();
            await _appManager.PopulateAsync(descriptor, app);
            items.Add(new
            {
                id = await _appManager.GetIdAsync(app),
                clientId = descriptor.ClientId,
                displayName = descriptor.DisplayName,
                consentType = descriptor.ConsentType,
                redirectUris = descriptor.RedirectUris.Select(u => u.ToString()),
            });
        }

        return Ok(new { total, page, pageSize, items });
    }

    // ── Get ───────────────────────────────────────────────────────────────

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        if (!_tenantContext.TenantId.HasValue) return BadRequest("Tenant not resolved.");

        var app = await FindOwnedAsync(id);
        if (app is null) return NotFound();

        var descriptor = new OpenIddictApplicationDescriptor();
        await _appManager.PopulateAsync(descriptor, app);

        return Ok(new
        {
            id = await _appManager.GetIdAsync(app),
            clientId = descriptor.ClientId,
            displayName = descriptor.DisplayName,
            consentType = descriptor.ConsentType,
            redirectUris = descriptor.RedirectUris.Select(u => u.ToString()),
            postLogoutRedirectUris = descriptor.PostLogoutRedirectUris.Select(u => u.ToString()),
            scopes = descriptor.Permissions
                .Where(p => p.StartsWith("scp:"))
                .Select(p => p[4..]),
        });
    }

    // ── Create ────────────────────────────────────────────────────────────

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] OidcClientCreateRequest req)
    {
        if (!_tenantContext.TenantId.HasValue) return BadRequest("Tenant not resolved.");

        var clientId = $"{_tenantContext.TenantId.Value}-{Guid.NewGuid():N}";
        var secret = GenerateSecret();

        var descriptor = BuildDescriptor(clientId, secret, req.DisplayName, req.RedirectUris,
            req.PostLogoutRedirectUris, req.Scopes);

        descriptor.Properties[TenantIdProperty] = JsonSerializer.SerializeToElement(_tenantContext.TenantId.Value.ToString());

        var app = await _appManager.CreateAsync(descriptor);
        var appId = await _appManager.GetIdAsync(app);

        await _auditLog.LogAsync(AuditEventTypes.OidcClientCreated, _tenantContext.TenantId,
            resourceType: "OidcClient", resourceId: appId, newValue: req.DisplayName);

        return Ok(new { id = appId, clientId, clientSecret = secret });
    }

    // ── Update ────────────────────────────────────────────────────────────

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] OidcClientUpdateRequest req)
    {
        if (!_tenantContext.TenantId.HasValue) return BadRequest("Tenant not resolved.");

        var app = await FindOwnedAsync(id);
        if (app is null) return NotFound();

        var descriptor = new OpenIddictApplicationDescriptor();
        await _appManager.PopulateAsync(descriptor, app);

        descriptor.DisplayName = req.DisplayName;
        descriptor.RedirectUris.Clear();
        foreach (var uri in req.RedirectUris)
            descriptor.RedirectUris.Add(new Uri(uri));

        descriptor.PostLogoutRedirectUris.Clear();
        foreach (var uri in req.PostLogoutRedirectUris)
            descriptor.PostLogoutRedirectUris.Add(new Uri(uri));

        // Rebuild scope permissions, keeping non-scope permissions intact.
        var nonScopePerms = descriptor.Permissions.Where(p => !p.StartsWith("scp:")).ToList();
        descriptor.Permissions.Clear();
        foreach (var p in nonScopePerms) descriptor.Permissions.Add(p);
        foreach (var scope in req.Scopes) descriptor.Permissions.Add($"scp:{scope}");

        await _appManager.UpdateAsync(app, descriptor);

        await _auditLog.LogAsync(AuditEventTypes.OidcClientUpdated, _tenantContext.TenantId,
            resourceType: "OidcClient", resourceId: id, newValue: req.DisplayName);

        return Ok();
    }

    // ── Delete ────────────────────────────────────────────────────────────

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        if (!_tenantContext.TenantId.HasValue) return BadRequest("Tenant not resolved.");

        var app = await FindOwnedAsync(id);
        if (app is null) return NotFound();

        var displayName = await _appManager.GetDisplayNameAsync(app);
        await _appManager.DeleteAsync(app);

        await _auditLog.LogAsync(AuditEventTypes.OidcClientDeleted, _tenantContext.TenantId,
            resourceType: "OidcClient", resourceId: id, oldValue: displayName);

        return NoContent();
    }

    // ── Rotate Secret ─────────────────────────────────────────────────────

    [HttpPost("{id}/rotate-secret")]
    public async Task<IActionResult> RotateSecret(string id)
    {
        if (!_tenantContext.TenantId.HasValue) return BadRequest("Tenant not resolved.");

        var app = await FindOwnedAsync(id);
        if (app is null) return NotFound();

        var descriptor = new OpenIddictApplicationDescriptor();
        await _appManager.PopulateAsync(descriptor, app);

        var newSecret = GenerateSecret();
        descriptor.ClientSecret = newSecret;

        await _appManager.UpdateAsync(app, descriptor);

        await _auditLog.LogAsync(AuditEventTypes.OidcClientUpdated, _tenantContext.TenantId,
            resourceType: "OidcClient", resourceId: id, newValue: "secret_rotated");

        return Ok(new { clientSecret = newSecret });
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private async Task<object?> FindOwnedAsync(string id)
    {
        var tenantId = _tenantContext.TenantId!.Value.ToString();

        await foreach (var app in _appManager.ListAsync())
        {
            if (await _appManager.GetIdAsync(app) != id) continue;
            var props = await _appManager.GetPropertiesAsync(app);
            if (props.TryGetValue(TenantIdProperty, out var tid) && tid.GetString() == tenantId)
                return app;
        }

        return null;
    }

    private static OpenIddictApplicationDescriptor BuildDescriptor(
        string clientId, string secret, string displayName,
        IEnumerable<string> redirectUris, IEnumerable<string> postLogoutRedirectUris,
        IEnumerable<string> scopes)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = secret,
            DisplayName = displayName,
            ConsentType = OpenIddictConstants.ConsentTypes.Explicit,
        };

        foreach (var uri in redirectUris)
            descriptor.RedirectUris.Add(new Uri(uri));

        foreach (var uri in postLogoutRedirectUris)
            descriptor.PostLogoutRedirectUris.Add(new Uri(uri));

        // Standard permissions for authorization_code + refresh_token flow.
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Authorization);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
        descriptor.Permissions.Add("ept:logout");
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.RefreshToken);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.ResponseTypes.Code);

        foreach (var scope in scopes)
            descriptor.Permissions.Add($"scp:{scope}");

        descriptor.Requirements.Add(OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange);

        return descriptor;
    }

    private static string GenerateSecret()
        => Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');
}

// ── Request DTOs ──────────────────────────────────────────────────────────────

public record OidcClientCreateRequest(
    string DisplayName,
    List<string> RedirectUris,
    List<string> PostLogoutRedirectUris,
    List<string> Scopes);

public record OidcClientUpdateRequest(
    string DisplayName,
    List<string> RedirectUris,
    List<string> PostLogoutRedirectUris,
    List<string> Scopes);
