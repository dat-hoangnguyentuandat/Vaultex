using System.Net;
using System.Net.Mail;
using App.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace App.Infrastructure.Services
{
    public class SmtpEmailSender : IEmailSender
    {
        private readonly IConfiguration _config;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task SendAsync(string to, string subject, string htmlBody)
        {
            var host = _config["Email:Smtp:Host"];

            if (string.IsNullOrEmpty(host))
            {
                // No SMTP configured — log the email so dev can see the reset link
                _logger.LogWarning("[EMAIL] To={To} | Subject={Subject} | Body={Body}", to, subject, htmlBody);
                return;
            }

            var port = int.Parse(_config["Email:Smtp:Port"] ?? "587");
            var user = _config["Email:Smtp:Username"] ?? "";
            var pass = _config["Email:Smtp:Password"] ?? "";
            var from = _config["Email:From"] ?? "noreply@vaultex.io";

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(user, pass)
            };

            var message = new MailMessage(from, to, subject, htmlBody)
            {
                IsBodyHtml = true
            };

            await client.SendMailAsync(message);
        }
    }
}
