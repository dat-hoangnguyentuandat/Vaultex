using App.Domain.Entities;
using App.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace App.API.Pages.Account;

public class ConfirmEmailModel : PageModel
{
    private readonly UserManager<User> _userManager;
    private readonly AuditLogService _auditLog;

    public ConfirmEmailModel(UserManager<User> userManager, AuditLogService auditLog)
    {
        _userManager = userManager;
        _auditLog = auditLog;
    }

    [BindProperty(SupportsGet = true)]
    public string Email { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string Token { get; set; } = string.Empty;

    public bool ShowSuccess { get; private set; }
    public bool InvalidLink { get; private set; }
    public bool AlreadyConfirmed { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrEmpty(Email) || string.IsNullOrEmpty(Token))
        {
            InvalidLink = true;
            return Page();
        }

        var user = await _userManager.FindByEmailAsync(Email);
        if (user is null)
        {
            InvalidLink = true;
            return Page();
        }

        if (user.EmailConfirmed)
        {
            AlreadyConfirmed = true;
            return Page();
        }

        var result = await _userManager.ConfirmEmailAsync(user, Token);
        if (!result.Succeeded)
        {
            InvalidLink = true;
            return Page();
        }

        await _auditLog.LogAsync(AuditEventTypes.EmailConfirmed, user.TenantId, user.Id,
            resourceType: "User", resourceId: user.Id.ToString());

        ShowSuccess = true;
        return Page();
    }
}
