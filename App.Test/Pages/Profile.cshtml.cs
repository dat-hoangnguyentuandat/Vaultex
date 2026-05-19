using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace App.Test.Pages;

[Authorize]
public class ProfileModel : PageModel
{
    public IReadOnlyList<Claim> Claims { get; private set; } = Array.Empty<Claim>();
    public string? AccessToken { get; private set; }
    public string? IdToken { get; private set; }
    public string? RefreshToken { get; private set; }

    public async Task OnGetAsync()
    {
        Claims = User.Claims.ToList();

        AccessToken = await HttpContext.GetTokenAsync("access_token");
        IdToken = await HttpContext.GetTokenAsync("id_token");
        RefreshToken = await HttpContext.GetTokenAsync("refresh_token");
    }
}
