using App.Application.DTOs;

namespace App.Application.Interfaces
{
    public interface IAuthService
    {
        Task<RegisterResponseDto> RegisterAsync(RegisterDto registerDto, RegisterPasswordDto registerPasswordDto, Guid? tenantId = null);
        Task ForgotPasswordAsync(ForgotPasswordDto forgotPasswordDto);
        Task ResetPasswordAsync(string email, string token, ResetPasswordDto resetPasswordDto);
        Task SendEmailConfirmationAsync(string email);

    }
}
