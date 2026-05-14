using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace App.Application.DTOs
{
    public class RegisterDto
    {
        [Required(ErrorMessage = "Vui lòng nhập email của bạn")]
        [EmailAddress(ErrorMessage = "Địa chỉ email không hợp lệ")]
        public String Email { get; set; } = string.Empty;
        [Required(ErrorMessage = "Vui lòng nhập số điện thoại của bạn")]
        [RegularExpression(@"^(0[35789]\d{8})$", ErrorMessage = "Vui lòng nhập đúng định dạng số điện thoại")]
        public String PhoneNumber { get; set; } = string.Empty;
        public String? Fullname { get; set; }
        public DateOnly? DateOfBirth { get; set; }
        public String? Company { get; set; }
        public String ? Position { get; set; }
    }

    public class RegisterPasswordDto : IValidatableObject
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
    public class RegisterRequestDto
    {
        public RegisterDto Info { get; set; } = new();
        public RegisterPasswordDto Password { get; set; } = new();
    }

}
