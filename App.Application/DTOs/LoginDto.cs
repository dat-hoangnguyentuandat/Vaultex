using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace App.Application.DTOs
{
    public class LoginDto
    {
        [Required(ErrorMessage = "Vui lòng nhập email của bạn")]
        [EmailAddress(ErrorMessage = "Vui lòng nhập đúng định dạng email")]
        public String Email { get; set; } = string.Empty;
        [Required(ErrorMessage = "Vui lòng nhập mật khẩu của bạn")]
        public String Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; }

    }
}
