using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace App.Test.Pages.Account;

public class LoginModel : PageModel
{
    public IActionResult OnGet(string? returnUrl = null)
    {
        var redirect = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : Url.Content("~/");

        return Challenge(
            new AuthenticationProperties { RedirectUri = redirect },
            OpenIdConnectDefaults.AuthenticationScheme);
    }
}
