using App.Application.DTOs;
using App.Application.Interfaces;
using App.Domain.Entities;
using App.Domain.Exceptions;
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
        private readonly IAuthService _authService;
        private readonly ITenantContext _tenantContext;
        private readonly UserManager<User> _userManager;
        private readonly AuditLogService _auditLog;

        public AuthController(
            IAuthService authService,
            ITenantContext tenantContext,
            UserManager<User> userManager,
            AuditLogService auditLog)
        {
            _authService = authService;
            _tenantContext = tenantContext;
            _userManager = userManager;
            _auditLog = auditLog;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
        {
            try
            {
                var result = await _authService.RegisterAsync(request.Info, request.Password, _tenantContext.TenantId);
                return Ok(result);
            }
            catch (ConflictException ex) { return Conflict(new { error = ex.Message }); }
            catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto forgotPasswordDto)
        {
            await _authService.ForgotPasswordAsync(forgotPasswordDto);
            return Ok("If the email exists, a reset link has been sent.");
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword(
            [FromQuery] string email,
            [FromQuery] string token,
            [FromBody] ResetPasswordDto resetPasswordDto)
        {
            try
            {
                await _authService.ResetPasswordAsync(email, token, resetPasswordDto);
                return Ok("Password reset successful.");
            }
            catch (NotFoundException ex) { return NotFound(new { error = ex.Message }); }
            catch (Exception ex) { return BadRequest(new { error = ex.Message }); }
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

        [HttpGet("profile")]
        [Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
        public async Task<IActionResult> GetProfile()
        {
            var userId = User.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;
            var user = userId is not null ? await _userManager.FindByIdAsync(userId) : null;
            if (user is null) return Unauthorized();

            return Ok(new
            {
                user.Id, user.Email, user.FullName, user.PhoneNumber,
                user.DateOfBirth, user.Company, user.Position,
                user.CreatedAt, user.LastLoginAt
            });
        }

        [HttpPut("profile")]
        [Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest req)
        {
            var userId = User.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;
            var user = userId is not null ? await _userManager.FindByIdAsync(userId) : null;
            if (user is null) return Unauthorized();

            user.FullName = req.FullName ?? user.FullName;
            user.PhoneNumber = req.PhoneNumber ?? user.PhoneNumber;
            user.DateOfBirth = req.DateOfBirth ?? user.DateOfBirth;
            user.Company = req.Company ?? user.Company;
            user.Position = req.Position ?? user.Position;

            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
                return BadRequest(result.Errors.Select(e => e.Description));

            await _auditLog.LogAsync(AuditEventTypes.ProfileUpdated, _tenantContext.TenantId, user.Id,
                resourceType: "User", resourceId: user.Id.ToString());

            return Ok(new { user.Id, user.Email, user.FullName });
        }

        [HttpPost("resend-confirmation")]
        public async Task<IActionResult> ResendConfirmation([FromBody] ForgotPasswordDto dto)
        {
            await _authService.SendEmailConfirmationAsync(dto.Email);
            return Ok("If the email exists and is unconfirmed, a confirmation link has been resent.");
        }

        [HttpPost("change-password")]
        [Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
        {
            var userId = User.FindFirst(OpenIddictConstants.Claims.Subject)?.Value;
            var user = userId is not null ? await _userManager.FindByIdAsync(userId) : null;
            if (user is null) return Unauthorized();

            var result = await _userManager.ChangePasswordAsync(user, req.CurrentPassword, req.NewPassword);
            if (!result.Succeeded)
                return BadRequest(result.Errors.Select(e => e.Description));

            await _auditLog.LogAsync(AuditEventTypes.PasswordChanged, _tenantContext.TenantId, user.Id,
                resourceType: "User", resourceId: user.Id.ToString());

            return Ok();
        }
    }

    public record UpdateProfileRequest(
        string? FullName,
        string? PhoneNumber,
        DateOnly? DateOfBirth,
        string? Company,
        string? Position);

    public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
}
