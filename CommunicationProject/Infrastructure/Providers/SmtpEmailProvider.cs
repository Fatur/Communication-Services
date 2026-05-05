using System;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using CommunicationServices.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CommunicationServices.Infrastructure.Providers
{
    public class SmtpEmailProvider : IEmailProvider
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<SmtpEmailProvider> _logger;

        public SmtpEmailProvider(IConfiguration configuration, ILogger<SmtpEmailProvider> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendAsync(string to, string body)
        {
            var host = _configuration["Smtp:Host"]
                ?? throw new InvalidOperationException("Smtp:Host is not configured.");
            var port = int.Parse(_configuration["Smtp:Port"] ?? "587");
            var username = _configuration["Smtp:Username"]
                ?? throw new InvalidOperationException("Smtp:Username is not configured.");
            var password = _configuration["Smtp:Password"]
                ?? throw new InvalidOperationException("Smtp:Password is not configured.");
            var senderEmail = _configuration["Smtp:SenderEmail"]
                ?? throw new InvalidOperationException("Smtp:SenderEmail is not configured.");
            var senderName = _configuration["Smtp:SenderName"] ?? "No Reply";
            var subject = _configuration["Smtp:DefaultSubject"] ?? "No Subject";
            var enableSsl = bool.Parse(_configuration["Smtp:EnableSsl"] ?? "true");

            _logger.LogInformation("Sending email to {To} via {Host}:{Port}", to, host, port);

            try
            {
                using var client = new SmtpClient(host, port)
                {
                    Credentials = new NetworkCredential(username, password),
                    EnableSsl = enableSsl
                };

                using var message = new MailMessage
                {
                    From = new MailAddress(senderEmail, senderName),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = false
                };
                message.To.Add(to);

                await client.SendMailAsync(message);

                _logger.LogInformation("Email successfully sent to {To}", to);
            }
            catch (SmtpException ex)
            {
                _logger.LogError(ex, "Failed to send email to {To}. SmtpStatusCode: {StatusCode}", to, ex.StatusCode);
                throw new InvalidOperationException($"Failed to send email to '{to}': {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error sending email to {To}", to);
                throw;
            }
        }
    }
}
