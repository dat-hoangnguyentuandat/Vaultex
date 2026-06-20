using App.Application.DTOs;
using App.Domain.Entities;
using App.Infrastructure.Data;
using App.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace App.API.Pages.Account;

public class LoginModel : PageModel
{
    private readonly SignInManager<User> _signInManager;
    private readonly UserManager<User> _userManager;
    private readonly AuditLogService _auditLog;
    private readonly PlatformDbContext _platformDb;

    public LoginModel(
        SignInManager<User> signInManager,
        UserManager<User> userManager,
        AuditLogService auditLog,
        PlatformDbContext platformDb)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _auditLog = auditLog;
        _platformDb = platformDb;
    }

    [BindProperty]
    public LoginDto Input { get; set; } = new();

    public string? ReturnUrl { get; set; }
    public string? Tenant { get; set; }
    public string? ErrorMessage { get; set; }
    public bool EmailNotConfirmed { get; private set; }

    public void OnGet(string? returnUrl = null, string? error = null, string? tenant = null)
    {
        ReturnUrl = returnUrl;
        Tenant = NormalizeTenant(tenant);

        ErrorMessage = error switch
        {
            "expired" => "Phiên đăng nhập đã hết hạn. Vui lòng thử lại.",
            "inactive" => "Tài khoản của bạn đã bị vô hiệu hoá.",
            _ => null
        };
    }

    [EnableRateLimiting("LoginSubmit")]
    public async Task<IActionResult> OnPostAsync(string? returnUrl = null, string? tenant = null)
    {
        ReturnUrl = returnUrl;
        Tenant = NormalizeTenant(tenant);

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

            if (user.TenantId.HasValue)
                destination = await AddTenantToDestinationAsync(destination, user.TenantId.Value);

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

    private static string? NormalizeTenant(string? tenant)
        => string.IsNullOrWhiteSpace(tenant) ? null : tenant.Trim().ToLowerInvariant();

    private async Task<string> AddTenantToDestinationAsync(string destination, Guid tenantId)
    {
        var subdomain = await _platformDb.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Subdomain)
            .FirstOrDefaultAsync();

        if (string.IsNullOrWhiteSpace(subdomain))
            return destination;

        Response.Cookies.Append("Vaultex.Tenant", subdomain, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            MaxAge = TimeSpan.FromMinutes(10)
        });

        var uri = new Uri(destination, UriKind.RelativeOrAbsolute);
        var path = uri.IsAbsoluteUri ? uri.AbsolutePath : destination.Split('?', 2)[0];
        var queryText = uri.IsAbsoluteUri
            ? uri.Query.TrimStart('?')
            : destination.Contains('?') ? destination.Split('?', 2)[1] : "";
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(queryText)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        query["tenant"] = subdomain;

        return Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(path, query!);
    }
}
