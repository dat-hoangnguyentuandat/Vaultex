using App.Application.DTOs;
using App.Domain.Entities;
using App.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace App.API.Pages.Account;

[EnableRateLimiting("LoginPage")]
public class LoginModel : PageModel
{
    private readonly SignInManager<User> _signInManager;
    private readonly UserManager<User> _userManager;
    private readonly AuditLogService _auditLog;

    public LoginModel(
        SignInManager<User> signInManager,
        UserManager<User> userManager,
        AuditLogService auditLog)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _auditLog = auditLog;
    }

    [BindProperty]
    public LoginDto Input { get; set; } = new();

    public string? ReturnUrl { get; set; }
    public string? ErrorMessage { get; set; }
    public bool EmailNotConfirmed { get; private set; }

    public void OnGet(string? returnUrl = null, string? error = null)
    {
        ReturnUrl = returnUrl;

        ErrorMessage = error switch
        {
            "expired" => "Phiên đăng nhập đã hết hạn. Vui lòng thử lại.",
            "inactive" => "Tài khoản của bạn đã bị vô hiệu hoá.",
            _ => null
        };
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
            return Page();

        var user = await _userManager.FindByEmailAsync(Input.Email);

        if (user is null || !user.IsActive)
        {
            await _auditLog.LogAsync(App.Domain.Entities.AuditEventTypes.LoginFailed, null);
            ModelState.AddModelError(string.Empty,
                "Email hoặc mật khẩu không chính xác. Vui lòng kiểm tra lại.");
            return Page();
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            var until = user.LockoutEnd!.Value.LocalDateTime.ToString("HH:mm");
            await _auditLog.LogAsync(App.Domain.Entities.AuditEventTypes.LoginFailed, user.TenantId, user.Id);
            ModelState.AddModelError(string.Empty,
                $"Tài khoản tạm thời bị khoá do đăng nhập sai nhiều lần. Vui lòng thử lại sau {until}.");
            return Page();
        }

        var result = await _signInManager.PasswordSignInAsync(
            user,
            Input.Password,
            isPersistent: Input.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            user.LastLoginAt = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);
            await _auditLog.LogAsync(App.Domain.Entities.AuditEventTypes.LoginSuccess, user.TenantId, user.Id);

            var destination = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? returnUrl
                : "/";

            return Redirect(destination);
        }

        if (result.IsNotAllowed)
        {
            EmailNotConfirmed = true;
            ModelState.AddModelError(string.Empty,
                "Email chưa được xác nhận. Vui lòng kiểm tra hộp thư và nhấn link xác nhận.");
            return Page();
        }

        if (result.IsLockedOut)
        {
            await _auditLog.LogAsync(App.Domain.Entities.AuditEventTypes.LoginFailed, user.TenantId, user.Id);
            ModelState.AddModelError(string.Empty,
                "Tài khoản tạm thời bị khoá do đăng nhập sai quá 5 lần. Vui lòng thử lại sau 15 phút.");
            return Page();
        }

        await _auditLog.LogAsync(App.Domain.Entities.AuditEventTypes.LoginFailed, user.TenantId, user.Id);
        ModelState.AddModelError(string.Empty,
            "Email hoặc mật khẩu không chính xác. Vui lòng kiểm tra lại.");
        return Page();
    }
}
