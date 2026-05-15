using App.Domain.Entities;
using App.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;

namespace App.API.Pages.Account;

[EnableRateLimiting("LoginPage")]
public class ResetPasswordModel : PageModel
{
    private readonly UserManager<User> _userManager;
    private readonly AuditLogService _auditLog;

    public ResetPasswordModel(UserManager<User> userManager, AuditLogService auditLog)
    {
        _userManager = userManager;
        _auditLog = auditLog;
    }

    [BindProperty(SupportsGet = true)]
    public string Email { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string Token { get; set; } = string.Empty;

    [BindProperty]
    public ResetPasswordInput Input { get; set; } = new();

    public bool ShowSuccess { get; private set; }
    public bool InvalidLink { get; private set; }

    public void OnGet()
    {
        if (string.IsNullOrEmpty(Email) || string.IsNullOrEmpty(Token))
            InvalidLink = true;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrEmpty(Email) || string.IsNullOrEmpty(Token))
        {
            InvalidLink = true;
            return Page();
        }

        if (!ModelState.IsValid)
            return Page();

        var user = await _userManager.FindByEmailAsync(Email);
        if (user is null || !user.IsActive)
        {
            ShowSuccess = true;
            return Page();
        }

        var result = await _userManager.ResetPasswordAsync(user, Token, Input.NewPassword);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }

        await _auditLog.LogAsync(AuditEventTypes.PasswordReset, user.TenantId, user.Id,
            resourceType: "User", resourceId: user.Id.ToString());

        ShowSuccess = true;
        return Page();
    }
}

public class ResetPasswordInput
{
    [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới")]
    [MinLength(8, ErrorMessage = "Mật khẩu phải có ít nhất 8 ký tự")]
    public string NewPassword { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu")]
    [Compare(nameof(NewPassword), ErrorMessage = "Mật khẩu không trùng khớp")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
