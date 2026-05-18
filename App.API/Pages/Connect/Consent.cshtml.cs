using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenIddict.Abstractions;

namespace App.API.Pages.Connect;

[Authorize]
public class ConsentModel : PageModel
{
    private readonly IOpenIddictApplicationManager _applicationManager;
    private readonly IOpenIddictScopeManager _scopeManager;

    public ConsentModel(
        IOpenIddictApplicationManager applicationManager,
        IOpenIddictScopeManager scopeManager)
    {
        _applicationManager = applicationManager;
        _scopeManager = scopeManager;
    }

    public string ApplicationName { get; private set; } = string.Empty;
    public IReadOnlyList<ScopeInfo> RequestedScopes { get; private set; } = [];
    public IReadOnlyDictionary<string, string> OidcParams { get; private set; } = new Dictionary<string, string>();

    public record ScopeInfo(string Name, string DisplayName);

    public async Task<IActionResult> OnGetAsync()
    {
        var clientId = Request.Query["client_id"].FirstOrDefault();
        if (string.IsNullOrEmpty(clientId))
            return BadRequest();

        var application = await _applicationManager.FindByClientIdAsync(clientId);
        if (application is null)
            return BadRequest();

        ApplicationName = await _applicationManager.GetDisplayNameAsync(application) ?? clientId;

        var scopeNames = Request.Query["scope"].ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var scopes = new List<ScopeInfo>();
        foreach (var name in scopeNames)
        {
            var scope = await _scopeManager.FindByNameAsync(name);
            var displayName = scope is not null
                ? await _scopeManager.GetDisplayNameAsync(scope) ?? name
                : name;
            scopes.Add(new ScopeInfo(name, displayName));
        }
        RequestedScopes = scopes;

        OidcParams = Request.Query
            .Where(q => q.Key != "consent")
            .ToDictionary(q => q.Key, q => q.Value.ToString());

        return Page();
    }
}
