using App.Application.DTOs;
using App.Application.Interfaces;
using App.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace App.API.Pages.Account;

public class RegisterModel : PageModel
{
    private readonly IAuthService _authService;
    private readonly ITenantContext _tenantContext;

    public RegisterModel(IAuthService authService, ITenantContext tenantContext)
    {
        _authService = authService;
        _tenantContext = tenantContext;
    }

    public int Step { get; private set; } = 1;
    public bool ShowSuccess { get; private set; }
    public List<string> ServerErrors { get; private set; } = [];
    public string? ReturnUrl { get; private set; }

    public RegisterDto Info { get; set; } = new();
    public RegisterPasswordDto Pwd { get; set; } = new();

    private void BindInfo()
    {
        Info = new RegisterDto
        {
            Email = Request.Form["Info.Email"].ToString(),
            PhoneNumber = Request.Form["Info.PhoneNumber"].ToString(),
            Fullname = Request.Form["Info.Fullname"],
            Company = Request.Form["Info.Company"],
            Position = Request.Form["Info.Position"],
        };
        if (DateOnly.TryParse(Request.Form["Info.DateOfBirth"], out var dob))
            Info.DateOfBirth = dob;
    }

    private void BindPwd()
    {
        Pwd = new RegisterPasswordDto
        {
            Password = Request.Form["Pwd.Password"].ToString(),
            ConfirmPassword = Request.Form["Pwd.ConfirmPassword"].ToString(),
        };
    }

    public static string EyeClosedSvg =>
        """<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M17.94 17.94A10.07 10.07 0 0 1 12 20c-7 0-11-8-11-8a18.45 18.45 0 0 1 5.06-5.94"/><path d="M9.9 4.24A9.12 9.12 0 0 1 12 4c7 0 11 8 11 8a18.5 18.5 0 0 1-2.16 3.19"/><line x1="1" y1="1" x2="23" y2="23"/></svg>""";

    public static string EyeOpenSvg =>
        """<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/></svg>""";

    public static string EyeClosedSvgJs => EyeClosedSvg.Replace("'", "\\'");
    public static string EyeOpenSvgJs => EyeOpenSvg.Replace("'", "\\'");

    public IActionResult OnGet(int step = 1, string? returnUrl = null)
    {
        if (step == 2)
        {
            var email = TempData.Peek("reg_email") as string;
            if (string.IsNullOrEmpty(email))
                return RedirectToPage();

            RestoreInfoFromTempData();
            Step = 2;
        }
        ReturnUrl = returnUrl ?? TempData.Peek("reg_returnUrl") as string;
        return Page();
    }

    public IActionResult OnPostInfo(string? returnUrl = null)
    {
        BindInfo();
        TryValidateModel(Info, nameof(Info));

        var errors = ModelState.Where(x => x.Value?.Errors.Count > 0)
                               .Select(x => $"{x.Key}: {x.Value!.Errors[0].ErrorMessage}")
                               .ToList();

        if (!ModelState.IsValid)
        {
            Step = 1;
            ReturnUrl = returnUrl;
            return Page();
        }

        TempData["reg_email"] = Info.Email;
        TempData["reg_phone"] = Info.PhoneNumber;
        TempData["reg_name"] = Info.Fullname;
        TempData["reg_dob"] = Info.DateOfBirth?.ToString("yyyy-MM-dd");
        TempData["reg_co"] = Info.Company;
        TempData["reg_pos"] = Info.Position;
        if (!string.IsNullOrEmpty(returnUrl))
            TempData["reg_returnUrl"] = returnUrl;

        return RedirectToPage(new { step = 2, returnUrl });
    }

    [EnableRateLimiting("RegistrationSubmit")]
    public async Task<IActionResult> OnPostRegisterAsync(string? returnUrl = null)
    {
        var email = TempData["reg_email"] as string;

        if (string.IsNullOrWhiteSpace(email))
            return RedirectToPage();

        BindPwd();
        TryValidateModel(Pwd, nameof(Pwd));

        TempData.Keep();

        ReturnUrl = returnUrl ?? TempData.Peek("reg_returnUrl") as string;

        if (!ModelState.IsValid)
        {
            Step = 2;
            RestoreInfoFromTempData();
            return Page();
        }

        var registerDto = new RegisterDto
        {
            Email = email,
            PhoneNumber = TempData.Peek("reg_phone") as string ?? string.Empty,
            Fullname = TempData.Peek("reg_name") as string,
            Company = TempData.Peek("reg_co") as string,
            Position = TempData.Peek("reg_pos") as string,
            DateOfBirth = DateOnly.TryParse(TempData.Peek("reg_dob") as string, out var dob) ? dob : null,
        };

        var passwordDto = new RegisterPasswordDto
        {
            Password = Pwd.Password,
            ConfirmPassword = Pwd.ConfirmPassword
        };

        try
        {
            await _authService.RegisterAsync(registerDto, passwordDto, _tenantContext.TenantId);

            ShowSuccess = true;
            Step = 2;
            return Page();
        }
        catch (Exception ex)
        {
            ServerErrors.Add(ex.Message);
            Step = 2;
            RestoreInfoFromTempData();
            return Page();
        }
    }

    private void RestoreInfoFromTempData()
    {
        Info = new RegisterDto
        {
            Email = TempData.Peek("reg_email") as string ?? string.Empty,
            PhoneNumber = TempData.Peek("reg_phone") as string ?? string.Empty,
            Fullname = TempData.Peek("reg_name") as string,
            Company = TempData.Peek("reg_co") as string,
            Position = TempData.Peek("reg_pos") as string,
            DateOfBirth = DateOnly.TryParse(TempData.Peek("reg_dob") as string, out var d) ? d : null,
        };
    }

    private static string TranslateError(IdentityError error) => error.Code switch
    {
        "DuplicateEmail" => "Email này đã được sử dụng.",
        "DuplicateUserName" => "Email này đã được sử dụng.",
        "InvalidEmail" => "Email không hợp lệ.",
        "PasswordTooShort" => "Mật khẩu phải có ít nhất 8 ký tự.",
        "PasswordRequiresDigit" => "Mật khẩu phải chứa ít nhất 1 chữ số.",
        "PasswordRequiresLower" => "Mật khẩu phải chứa ít nhất 1 chữ thường.",
        "PasswordRequiresUpper" => "Mật khẩu phải chứa ít nhất 1 chữ hoa.",
        "PasswordRequiresNonAlphanumeric" => "Mật khẩu phải chứa ít nhất 1 ký tự đặc biệt.",
        _ => error.Description,
    };
}
