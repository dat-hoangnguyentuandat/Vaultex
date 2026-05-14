using App.API.Controllers;
using App.Application.DTOs;
using App.Application.Interfaces;
using App.Domain.Entities;
using App.Infrastructure.Services;
using App.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

namespace App.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly ILogger<AuthController> _logger;
        private readonly IAuthService _authService;
        private readonly ITenantContext _tenantContext;

        public AuthController(
            ILogger<AuthController> logger,
            IAuthService authService,
            ITenantContext tenantContext)
        {
            _logger = logger;
            _authService = authService;
            _tenantContext = tenantContext;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
        {
            var result = await _authService.RegisterAsync(request.Info, request.Password, _tenantContext.TenantId);

            _logger.LogInformation(
                "[AUDIT] REGISTER | Email={Email} | TenantId={TenantId}",
                result.Email, _tenantContext.TenantId?.ToString() ?? "none");

            return Ok(result);
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto forgotPasswordDto)
        {
            await _authService.ForgotPasswordAsync(forgotPasswordDto);

            _logger.LogInformation(
                "[AUDIT] PASSWORD_RESET_REQUESTED | Email={Email}",
                forgotPasswordDto.Email);

            return Ok("Nếu email tồn tại, link reset đã được gửi");
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword(
            [FromQuery] string email,
            [FromQuery] string token,
            [FromBody] ResetPasswordDto resetPasswordDto)
        {
            await _authService.ResetPasswordAsync(email, token, resetPasswordDto);

            _logger.LogWarning(
                "[AUDIT] SECURITY | Event=PASSWORD_RESET_SUCCESS | Email={Email} | Description=Password was successfully reset via API.",
                email);

            return Ok("Đặt lại mật khẩu thành công");
        }

        [HttpGet("permissions")]
        [Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
        public IActionResult GetUserPermissions()
        {
            var permissions = User.FindAll("permission").Select(c => c.Value).ToList();
            var roles = User.FindAll(OpenIddictConstants.Claims.Role).Select(c => c.Value).ToList();

            return Ok(new
            {
                roles = roles,
                permissions = permissions
            });
        }
    }
}
