using App.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;

namespace App.API.Pages.Account;

public class ResendConfirmationModel : PageModel
{
    private readonly IAuthService _authService;

    public ResendConfirmationModel(IAuthService authService)
    {
        _authService = authService;
    }

    [BindProperty]
    public ResendConfirmationInput Input { get; set; } = new();

    public bool EmailSent { get; private set; }

    public void OnGet() { }

    [EnableRateLimiting("ConfirmationEmail")]
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        await _authService.SendEmailConfirmationAsync(Input.Email);

        EmailSent = true;
        return Page();
    }
}

public class ResendConfirmationInput
{
    [Required(ErrorMessage = "Vui lòng nhập email")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ")]
    public string Email { get; set; } = string.Empty;
}
