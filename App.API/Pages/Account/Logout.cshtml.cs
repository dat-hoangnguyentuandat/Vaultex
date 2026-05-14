using App.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OpenIddict.Abstractions;

namespace App.API.Pages.Account;

public sealed class LogoutModel : PageModel
{
    private readonly SignInManager<User> _signInManager;
    private readonly UserManager<User> _userManager;
    private readonly IOpenIddictTokenManager _tokenManager;
    private readonly ILogger<LogoutModel> _logger;

    public LogoutModel(
        SignInManager<User> signInManager,
        UserManager<User> userManager,
        IOpenIddictTokenManager tokenManager,
        ILogger<LogoutModel> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _tokenManager = tokenManager;
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var userId = _userManager.GetUserId(User);
        var username = _userManager.GetUserName(User) ?? userId ?? "unknown";

        if (!string.IsNullOrEmpty(userId))
        {
            await foreach (var token in _tokenManager.FindBySubjectAsync(userId))
                await _tokenManager.TryRevokeAsync(token);
        }

        await _signInManager.SignOutAsync();

        _logger.LogInformation(
            "[AUDIT] LOGOUT | UserId={UserId} | Username={Username}",
            userId ?? "unknown", username);

        return Redirect("/account/login");
    }

    public async Task<IActionResult> OnPostAsync() => await OnGetAsync();
}
