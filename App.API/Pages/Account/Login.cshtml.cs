using App.Application.DTOs;
using App.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace App.API.Pages.Account;

public class LoginModel : PageModel
{
    private readonly SignInManager<User> _signInManager;
    private readonly UserManager<User> _userManager;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(
        SignInManager<User> signInManager,
        UserManager<User> userManager,
        ILogger<LoginModel> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _logger = logger;
    }

    [BindProperty]
    public LoginDto Input { get; set; } = new();

    public string? ReturnUrl { get; set; }
    public string? ErrorMessage { get; set; }

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

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var user = await _userManager.FindByEmailAsync(Input.Email);

        if (user is null || !user.IsActive)
        {
            _logger.LogWarning(
                "[AUDIT] SECURITY | Event=LOGIN_INACTIVE_OR_NOT_FOUND | Email={Email} | IP={IpAddress}",
                Input.Email, ip);

            ModelState.AddModelError(string.Empty,
                "Email hoặc mật khẩu không chính xác. Vui lòng kiểm tra lại.");
            return Page();
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            var until = user.LockoutEnd!.Value.LocalDateTime.ToString("HH:mm");

            _logger.LogWarning(
                "[AUDIT] SECURITY | Event=LOGIN_BLOCKED_LOCKOUT | UserId={UserId} | Email={Email} | IP={IpAddress} | Until={Until}",
                user.Id.ToString(), user.Email, ip, until);

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

            _logger.LogInformation(
                "[AUDIT] LOGIN_SUCCESS | UserId={UserId} | Email={Email} | IP={IpAddress}",
                user.Id.ToString(), user.Email, ip);

            var destination = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
                ? returnUrl
                : "/";

            return Redirect(destination);
        }

        if (result.IsLockedOut)
        {
            _logger.LogWarning(
                "[AUDIT] SECURITY | Event=LOGIN_TRIGGERED_LOCKOUT | UserId={UserId} | Email={Email} | IP={IpAddress}",
                user.Id.ToString(), user.Email, ip);

            ModelState.AddModelError(string.Empty,
                "Tài khoản tạm thời bị khoá do đăng nhập sai quá 5 lần. Vui lòng thử lại sau 15 phút.");
            return Page();
        }

        _logger.LogWarning(
            "[AUDIT] LOGIN_FAILED | UserId={UserId} | Email={Email} | IP={IpAddress}",
            user.Id.ToString(), user.Email, ip);

        ModelState.AddModelError(string.Empty,
            "Email hoặc mật khẩu không chính xác. Vui lòng kiểm tra lại.");
        return Page();
    }
}
