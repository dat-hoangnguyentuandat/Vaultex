using App.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;

namespace App.API.Pages.Account;

public class ForgotPasswordModel : PageModel
{
    private readonly UserManager<User> _userManager;
    private readonly ILogger<ForgotPasswordModel> _logger;

    public ForgotPasswordModel(UserManager<User> userManager, ILogger<ForgotPasswordModel> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    [BindProperty]
    public ForgotPasswordInput Input { get; set; } = new();

    [BindProperty]
    public ResetPasswordInput Reset { get; set; } = new();

    public bool ShowSuccess { get; private set; }
    public bool EmailFound { get; private set; }

    public void OnGet() { }

    public async Task<IActionResult> OnPostCheckEmailAsync()
    {
        ModelState.Remove("NewPassword");
        ModelState.Remove("ConfirmPassword");

        if (!ModelState.IsValid)
            return Page();

        var user = await _userManager.FindByEmailAsync(Input.Email);

        if (user == null || !user.IsActive)
        {
            _logger.LogWarning(
                "[AUDIT] SECURITY | Event=PASSWORD_RESET_UNKNOWN_EMAIL | Email={Email}",
                Input.Email);

            ModelState.AddModelError(string.Empty, "Email không tồn tại trong hệ thống.");
            return Page();
        }

        _logger.LogInformation(
            "[AUDIT] PASSWORD_RESET_REQUESTED | UserId={UserId} | Email={Email}",
            user.Id.ToString(), user.Email);

        EmailFound = true;
        return Page();
    }

    public async Task<IActionResult> OnPostResetPasswordAsync()
    {
        ModelState.Remove("Email");

        if (!ModelState.IsValid)
        {
            EmailFound = true;
            return Page();
        }

        var user = await _userManager.FindByEmailAsync(Input.Email);

        if (user == null || !user.IsActive)
        {
            ModelState.AddModelError(string.Empty, "Có lỗi xảy ra, vui lòng thử lại.");
            EmailFound = true;
            return Page();
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, Reset.NewPassword);

        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            EmailFound = true;
            return Page();
        }

        _logger.LogWarning(
            "[AUDIT] SECURITY | Event=PASSWORD_RESET_SUCCESS | UserId={UserId} | Email={Email}",
            user.Id.ToString(), user.Email);

        ShowSuccess = true;
        return Page();
    }
}

public class ForgotPasswordInput
{
    [Required(ErrorMessage = "Vui lòng nhập email")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ")]
    public string Email { get; set; } = string.Empty;
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
