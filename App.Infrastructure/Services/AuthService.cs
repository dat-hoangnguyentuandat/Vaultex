using App.Application.DTOs;
using App.Application.Interfaces;
using App.Domain.Constants;
using App.Domain.Entities;
using App.Domain.Exceptions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;

namespace App.Infrastructure.Services
{
    public class AuthService : IAuthService
    {
        private readonly UserManager<User> _userManager;
        private readonly IEmailSender _emailSender;
        private readonly IConfiguration _config;
        private readonly AuditLogService _auditLog;

        public AuthService(
            UserManager<User> userManager,
            IEmailSender emailSender,
            IConfiguration config,
            AuditLogService auditLog)
        {
            _userManager = userManager;
            _emailSender = emailSender;
            _config = config;
            _auditLog = auditLog;
        }

        public async Task ForgotPasswordAsync(ForgotPasswordDto forgotPasswordDto)
        {
            var user = await _userManager.FindByEmailAsync(forgotPasswordDto.Email);
            if (user == null) return;

            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var encodedToken = Uri.EscapeDataString(token);
            var baseUrl = _config["App:BaseUrl"] ?? "https://localhost:7108";
            var resetLink = $"{baseUrl}/account/reset-password?email={Uri.EscapeDataString(user.Email!)}&token={encodedToken}";

            await _emailSender.SendAsync(
                to: user.Email!,
                subject: "Reset your Vaultex password",
                htmlBody: $"""
                    <p>You requested a password reset for your Vaultex account.</p>
                    <p><a href="{resetLink}">Click here to reset your password</a></p>
                    <p>This link expires in 24 hours. If you did not request this, ignore this email.</p>
                    """);
        }

        public async Task<RegisterResponseDto> RegisterAsync(RegisterDto registerDto, RegisterPasswordDto registerPasswordDto, Guid? tenantId = null)
        {
            var existingUser = await _userManager.FindByEmailAsync(registerDto.Email);
            if (existingUser != null)
                throw new ConflictException("Email is already in use.");

            var user = new User
            {
                UserName = registerDto.Email,
                Email = registerDto.Email,
                PhoneNumber = registerDto.PhoneNumber,
                FullName = registerDto.Fullname,
                DateOfBirth = registerDto.DateOfBirth,
                Company = registerDto.Company,
                Position = registerDto.Position,
                TenantId = tenantId
            };

            var result = await _userManager.CreateAsync(user, registerPasswordDto.Password);
            if (!result.Succeeded)
                throw new Exception(string.Join(", ", result.Errors.Select(e => e.Description)));

            // Assign Viewer role scoped to tenant if tenantId provided, else global Viewer
            var roleName = tenantId.HasValue ? $"{tenantId}:{Roles.Viewer}" : Roles.Viewer;
            await _userManager.AddToRoleAsync(user, roleName);

            await _auditLog.LogAsync(AuditEventTypes.Register, tenantId, user.Id,
                resourceType: "User", resourceId: user.Id.ToString());

            await SendEmailConfirmationAsync(user);

            return new RegisterResponseDto
            {
                Message = "Registration successful. Please check your email to confirm your account.",
                Email = user.Email!
            };
        }

        public async Task SendEmailConfirmationAsync(string email)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null || user.EmailConfirmed) return;
            await SendEmailConfirmationAsync(user);
        }

        private async Task SendEmailConfirmationAsync(User user)
        {
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            var encodedToken = Uri.EscapeDataString(token);
            var baseUrl = _config["App:BaseUrl"] ?? "https://localhost:7108";
            var confirmLink = $"{baseUrl}/account/confirm-email?email={Uri.EscapeDataString(user.Email!)}&token={encodedToken}";

            await _emailSender.SendAsync(
                to: user.Email!,
                subject: "Xác nhận tài khoản Vaultex",
                htmlBody: $"""
                    <p>Cảm ơn bạn đã đăng ký tài khoản Vaultex.</p>
                    <p><a href="{confirmLink}">Nhấn vào đây để xác nhận email của bạn</a></p>
                    <p>Link này có hiệu lực trong 24 giờ. Nếu bạn không đăng ký, hãy bỏ qua email này.</p>
                    """);
        }

        public async Task ResetPasswordAsync(string email, string token, ResetPasswordDto resetPasswordDto)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
                throw new NotFoundException("Account not found.");

            var result = await _userManager.ResetPasswordAsync(user, token, resetPasswordDto.Password);
            if (!result.Succeeded)
                throw new Exception(string.Join(", ", result.Errors.Select(e => e.Description)));

            await _auditLog.LogAsync(AuditEventTypes.PasswordReset, user.TenantId, user.Id,
                resourceType: "User", resourceId: user.Id.ToString());
        }
    }
}
