using CommunicationServices.Application.DTOs;
using CommunicationServices.Application.Interfaces;
using CommunicationServices.Domain.Entities;
using CommunicationServices.Infrastructure.Enum;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Data;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace CommunicationServices.Infrastructure.Providers
{
    public class EmailProvider : IEmailProvider
    {
        private IConnectionFactory _connectionFactory;
        private IConfiguration _configuration;
        private readonly ILogger<EmailProvider> _logger;
        public EmailProvider(IConnectionFactory connectionFactory, IConfiguration configuration, ILogger<EmailProvider> logger)
        {
            _connectionFactory = connectionFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public Task SendAsync(MessageLog message, string body)
        {
            _logger.LogInformation("[EmailProvider] Sending email to {To}: {Body}", message.Recipients, body);

            try
            {
                if (message.Recipients.Count == 0 || string.IsNullOrEmpty(body))
                    throw new Exception("recipients and message body are required.");


            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EmailProvider] Failed to send email to {To}: {Body}", message.Recipients, body);
                throw;
            }

            // Simulate success
            return Task.CompletedTask;
        }

        //private async Task SendViaSMTPServer(MessageLog message, string body)
        //{
        //    MailMessage mailMessage = new MailMessage();
        //    SmtpClient SMTP = new SmtpClient();
        //    mailMessage.From = new MailAddress(Username, Name);
        //    foreach (var receipent in message.Recipients)
        //    {
        //        mailMessage.To.Add(new MailAddress(receipent));
        //    }

        //    EmailPayload? payload = null;
        //    if (!string.IsNullOrEmpty(message.EmailJson))
        //        payload = JsonConvert.DeserializeObject<EmailPayload>(message.EmailJson);

        //    foreach (var address in payload?.CC ?? Array.Empty<string>())
        //    {
        //        var addressTrim = address.Trim();
        //        if (!string.IsNullOrEmpty(addressTrim))
        //            mailMessage.CC.Add(new MailAddress(addressTrim));
        //    }

        //    mailMessage.Subject = payload?.Subject ?? "No Subject";
        //    mailMessage.Body = body;

        //    if (!string.IsNullOrEmpty(message.AttachmentPaths))
        //    {
        //        IList<string> attachmentPaths = message.AttachmentPaths.Split(';', StringSplitOptions.RemoveEmptyEntries);
        //        foreach (var path in attachmentPaths)
        //        {
        //            if (File.Exists(path))
        //                mailMessage.Attachments.Add(new System.Net.Mail.Attachment(path));
        //        }
        //    }

        //    mailMessage.IsBodyHtml = payload?.IsHtml ?? false;
        //    SMTP.Port = 587;
        //    SMTP.Credentials = credential;
        //    SMTP.Host = Host;
        //    SMTP.EnableSsl = true;
        //    SMTP.UseDefaultCredentials = false;
        //    SMTP.DeliveryMethod = SmtpDeliveryMethod.Network;
        //    await SMTP.SendMailAsync(mailMessage).ConfigureAwait(false);
        //}

        private async Task SendViaSendGrid(MessageLog message, string body)
        {
            var apiKey = _configuration["SendGrid:ApiKey"];
            var client = new SendGridClient(apiKey);

            EmailPayload? payload = null;
            if (!string.IsNullOrEmpty(message.EmailJson))
                payload = JsonConvert.DeserializeObject<EmailPayload>(message.EmailJson);

            var mailMessage = new SendGridMessage()
            {
                From = new EmailAddress("no-reply@soficloud.com", "SofiCloud"),
                Subject = payload?.Subject ?? "No Subject",
                PlainTextContent = body,
                HtmlContent = payload?.IsHtml == true ? body : null
            };

            foreach (var address in message.Recipients)
            {
                var addressTrim = address.Trim();
                mailMessage.AddTo(new EmailAddress(addressTrim));
            }

            foreach (var address in payload?.CC ?? Array.Empty<string>())
            {
                var addressTrim = address.Trim();
                if (!string.IsNullOrEmpty(addressTrim))
                    mailMessage.AddCc(new EmailAddress(addressTrim));
            }

            if (!string.IsNullOrEmpty(message.AttachmentPaths))
            {
                IList<string> attachmentPaths = message.AttachmentPaths.Split(';', StringSplitOptions.RemoveEmptyEntries);
                foreach (var path in attachmentPaths)
                {
                    if (File.Exists(path))
                    {
                        var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                        var attachment = new SendGrid.Helpers.Mail.Attachment
                        {
                            Content = Convert.ToBase64String(bytes),
                            Filename = Path.GetFileName(path),
                            Type = "application/octet-stream",
                            Disposition = "attachment"
                        };
                        mailMessage.Attachments.Add(attachment);
                    }
                }
            }

            var response = await client.SendEmailAsync(mailMessage).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.Accepted)
            {
                throw new ApplicationException(response.StatusCode.ToString());
            }
        }
    }
}
