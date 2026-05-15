using App.Domain.Entities;
using App.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenIddict.Abstractions;

namespace App.API.Pages.Account;

public sealed class LogoutModel : PageModel
{
    private readonly SignInManager<User> _signInManager;
    private readonly UserManager<User> _userManager;
    private readonly IOpenIddictTokenManager _tokenManager;
    private readonly TokenBlacklistService _blacklist;
    private readonly AuditLogService _auditLog;

    public LogoutModel(
        SignInManager<User> signInManager,
        UserManager<User> userManager,
        IOpenIddictTokenManager tokenManager,
        TokenBlacklistService blacklist,
        AuditLogService auditLog)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _tokenManager = tokenManager;
        _blacklist = blacklist;
        _auditLog = auditLog;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var userId = _userManager.GetUserId(User);

        var accessToken = await HttpContext.GetTokenAsync("access_token");
        if (!string.IsNullOrEmpty(accessToken))
        {
            try
            {
                var jwt = new JsonWebToken(accessToken);
                var jti = jwt.Id;
                var exp = jwt.ValidTo;
                if (!string.IsNullOrEmpty(jti) && exp > DateTime.UtcNow)
                    await _blacklist.BlacklistAsync(jti, new DateTimeOffset(exp));
            }
            catch { /* malformed token — skip blacklisting */ }
        }

        if (!string.IsNullOrEmpty(userId))
        {
            await foreach (var token in _tokenManager.FindBySubjectAsync(userId))
                await _tokenManager.TryRevokeAsync(token);

            if (Guid.TryParse(userId, out var userGuid))
            {
                var user = await _userManager.FindByIdAsync(userId);
                await _auditLog.LogAsync(AuditEventTypes.Logout, user?.TenantId, userGuid);
            }
        }

        await _signInManager.SignOutAsync();

        return Redirect("/account/login");
    }

    public async Task<IActionResult> OnPostAsync() => await OnGetAsync();
}
