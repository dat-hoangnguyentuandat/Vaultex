using App.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;

namespace App.API.Pages.Account;

public class ForgotPasswordModel : PageModel
{
    private readonly IAuthService _authService;

    public ForgotPasswordModel(IAuthService authService)
    {
        _authService = authService;
    }

    [BindProperty]
    public ForgotPasswordInput Input { get; set; } = new();

    public bool EmailSent { get; private set; }

    public void OnGet() { }

    [EnableRateLimiting("PasswordRecovery")]
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        // Always call ForgotPasswordAsync — it silently ignores unknown emails
        // to prevent user enumeration
        await _authService.ForgotPasswordAsync(new App.Application.DTOs.ForgotPasswordDto
        {
            Email = Input.Email
        });

        EmailSent = true;
        return Page();
    }
}

public class ForgotPasswordInput
{
    [Required(ErrorMessage = "Vui lòng nhập email")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ")]
    public string Email { get; set; } = string.Empty;
}
