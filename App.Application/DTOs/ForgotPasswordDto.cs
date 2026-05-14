using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace App.Application.DTOs
{
    public class ForgotPasswordDto
    {
        [Required(ErrorMessage = "Vui lòng nhập email của bạn")]
        [EmailAddress(ErrorMessage = "Vui lòng nhập đúng dạng email")]
        public String Email { get; set; } = string.Empty;

    }

    public class ResetPasswordDto : IValidatableObject
    {
        [Required(ErrorMessage = "Vui lòng nhập mật khẩu")]
        [MinLength(8, ErrorMessage = "Sử dụng tối thiểu 8 ký tự, bao gồm chữ cái, chữ số và ký tự đặc biệt")]
        public String Password { get; set; } = string.Empty;
        [Required(ErrorMessage = "Vui lòng nhập mật khẩu")]
        public String ConfirmPassword { get; set; } = string.Empty;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (!string.IsNullOrEmpty(Password)
            && !string.IsNullOrEmpty(ConfirmPassword)
            && Password != ConfirmPassword)
            {
                yield return new ValidationResult(
                    "Mật khẩu không trùng khớp",
                    new[] { nameof(ConfirmPassword) });
            }

        }
    }
}
