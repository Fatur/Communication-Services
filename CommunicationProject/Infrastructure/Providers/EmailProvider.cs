using CommunicationServices.Application.DTOs;
using CommunicationServices.Application.Interfaces;
using CommunicationServices.Domain.Entities;
using CommunicationServices.Helper;
using CommunicationServices.Infrastructure.Enum;
using Dapper;
using Microsoft.Data.SqlClient;
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
        private const int SmtpPort = 587;
        private const string DefaultSenderName = "SofiCloud";
        private const string DefaultSenderEmail = "no-reply@soficloud.com";
        private const string DefaultSubject = "No Subject";

        private readonly IConnectionFactory _connectionFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailProvider> _logger;

        public EmailProvider(
            IConnectionFactory connectionFactory,
            IConfiguration configuration,
            ILogger<EmailProvider> logger)
        {
            _connectionFactory = connectionFactory;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task SendAsync(Requestor requestor, MessageLog message, string body)
        {
            _logger.LogInformation("[EmailProvider] Sending email to {To} via {Requestor}: {Body}", message.Recipients, requestor, body);

            try
            {
                if (message.Recipients.Count == 0 || string.IsNullOrEmpty(body))
                    throw new ArgumentException("Recipients and message body are required.");

                var credential = requestor switch
                {
                    Requestor.Soficloud => await GetSoficloudSmtpCredentialAsync(message.TenantId, message.WebMenuId).ConfigureAwait(false),
                    Requestor.Pisicloud => await GetPisicloudSmtpCredentialAsync(message.TenantId).ConfigureAwait(false),
                    _ => throw new NotSupportedException($"Unsupported requestor: {requestor}")
                };

                if (credential.HasValue)
                    await SendViaSmtpAsync(message, body, credential.Value).ConfigureAwait(false);
                else
                    await SendViaSendGridAsync(message, body).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[EmailProvider] Failed to send email to {To}: {Body}", message.Recipients, body);
                throw;
            }
        }

        // -------------------------------------------------------------------
        //  Payload / attachment helpers (shared by both send paths)
        // -------------------------------------------------------------------

        private static EmailPayload? ParsePayload(string? emailJson)
        {
            return string.IsNullOrEmpty(emailJson)
                ? null
                : JsonConvert.DeserializeObject<EmailPayload>(emailJson);
        }

        private static IEnumerable<string> GetValidAddresses(IEnumerable<string> addresses)
        {
            foreach (var address in addresses)
            {
                var trimmed = address.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    yield return trimmed;
            }
        }

        private static IList<string> ParseAttachmentPaths(string? attachmentPaths)
        {
            if (string.IsNullOrEmpty(attachmentPaths))
                return Array.Empty<string>();

            return attachmentPaths
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Where(File.Exists)
                .ToList();
        }

        // -------------------------------------------------------------------
        //  SMTP transport
        // -------------------------------------------------------------------

        private async Task SendViaSmtpAsync(MessageLog message, string body, SmtpCredential credential)
        {
            var payload = ParsePayload(message.EmailJson);

            using var mailMessage = BuildSmtpMessage(message, body, payload, credential);
            AttachFilesToSmtpMessage(mailMessage, message.AttachmentPaths);

            using var smtpClient = CreateSmtpClient(credential);
            await smtpClient.SendMailAsync(mailMessage).ConfigureAwait(false);
        }

        private static MailMessage BuildSmtpMessage(
            MessageLog message,
            string body,
            EmailPayload? payload,
            SmtpCredential credential)
        {
            var mail = new MailMessage
            {
                From = new MailAddress(credential.Email, credential.DisplayName),
                Subject = payload?.Subject ?? DefaultSubject,
                Body = body,
                IsBodyHtml = payload?.IsHtml ?? false
            };

            foreach (var recipient in message.Recipients)
                mail.To.Add(new MailAddress(recipient));

            foreach (var cc in GetValidAddresses(payload?.CC ?? Array.Empty<string>()))
                mail.CC.Add(new MailAddress(cc));

            return mail;
        }

        private static void AttachFilesToSmtpMessage(MailMessage mail, string? attachmentPaths)
        {
            foreach (var path in ParseAttachmentPaths(attachmentPaths))
                mail.Attachments.Add(new System.Net.Mail.Attachment(path));
        }

        private static SmtpClient CreateSmtpClient(SmtpCredential credential)
        {
            return new SmtpClient
            {
                Host = credential.Host,
                Port = SmtpPort,
                Credentials = credential.NetworkCredential,
                EnableSsl = true,
                UseDefaultCredentials = false,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };
        }

        // -------------------------------------------------------------------
        //  SendGrid transport
        // -------------------------------------------------------------------

        private async Task SendViaSendGridAsync(MessageLog message, string body)
        {
            var apiKey = _configuration["SendGrid:ApiKey"];
            var client = new SendGridClient(apiKey);

            var payload = ParsePayload(message.EmailJson);
            var mailMessage = BuildSendGridMessage(message, body, payload);

            await AttachFilesToSendGridMessageAsync(mailMessage, message.AttachmentPaths)
                .ConfigureAwait(false);

            var response = await client.SendEmailAsync(mailMessage).ConfigureAwait(false);

            if (response.StatusCode != HttpStatusCode.Accepted)
                throw new ApplicationException($"SendGrid returned {response.StatusCode}.");
        }

        private static SendGridMessage BuildSendGridMessage(
            MessageLog message,
            string body,
            EmailPayload? payload)
        {
            var mail = new SendGridMessage
            {
                From = new EmailAddress(DefaultSenderEmail, DefaultSenderName),
                Subject = payload?.Subject ?? DefaultSubject,
                PlainTextContent = body,
                HtmlContent = payload?.IsHtml == true ? body : null
            };

            foreach (var recipient in message.Recipients)
                mail.AddTo(new EmailAddress(recipient.Trim()));

            foreach (var cc in GetValidAddresses(payload?.CC ?? Array.Empty<string>()))
                mail.AddCc(new EmailAddress(cc));

            return mail;
        }

        private static async Task AttachFilesToSendGridMessageAsync(
            SendGridMessage mail,
            string? attachmentPaths)
        {
            foreach (var path in ParseAttachmentPaths(attachmentPaths))
            {
                var bytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                mail.Attachments.Add(new SendGrid.Helpers.Mail.Attachment
                {
                    Content = Convert.ToBase64String(bytes),
                    Filename = Path.GetFileName(path),
                    Type = "application/octet-stream",
                    Disposition = "attachment"
                });
            }
        }

        // -------------------------------------------------------------------
        //  Credential resolution (WebMenuSetup → SMTPServer → null/SendGrid)
        // -------------------------------------------------------------------

        private record struct SmtpCredential(
            NetworkCredential NetworkCredential,
            string Host,
            string DisplayName,
            string Email);

        private async Task<SmtpCredential?> GetSoficloudSmtpCredentialAsync(string tenantId, int? webMenuId)
        {
            // 1) Try tenant-specific WebMenu SMTP settings
            var fromWebMenu = await TryGetWebMenuCredentialAsync(tenantId, webMenuId).ConfigureAwait(false);
            if (fromWebMenu.HasValue)
                return fromWebMenu;

            // 2) Fall back to shared SMTP server config
            return await TryGetSharedSmtpCredentialAsync(tenantId).ConfigureAwait(false);
        }

        private async Task<SmtpCredential?> GetPisicloudSmtpCredentialAsync(string tenantId)
        {
            throw new NotSupportedException("Pisicloud requestor is not supported for email sending.");
        }

        private async Task<SmtpCredential?> TryGetWebMenuCredentialAsync(string tenantId, int? webMenuId)
        {
            using var connection = await _connectionFactory
                .GetConnectionAsync(Requestor.Soficloud, tenantId);

            const string sql =
                "SELECT SmtpHost, SmtpEmail, SmtpName, SmtpPassword " +
                "FROM tblwebmenusetup WHERE WebMenuId = @WebMenuId";

            var row = await connection
                .QuerySingleOrDefaultAsync<(string SmtpHost, string SmtpEmail, string SmtpName, string SmtpPassword)?>(
                    new CommandDefinition(sql, new { WebMenuId = webMenuId }))
                .ConfigureAwait(false);

            if (!row.HasValue
                || string.IsNullOrEmpty(row.Value.SmtpEmail)
                || string.IsNullOrEmpty(row.Value.SmtpPassword))
                return null;

            return new SmtpCredential(
                new NetworkCredential(row.Value.SmtpEmail, Encryptor.DecryptPassword(row.Value.SmtpPassword)),
                row.Value.SmtpHost,
                row.Value.SmtpName ?? DefaultSenderName,
                row.Value.SmtpEmail);
        }

        private async Task<SmtpCredential?> TryGetSharedSmtpCredentialAsync(string tenantId)
        {
            using var connection = new SqlConnection(
                _configuration.GetConnectionString(Requestor.Soficloud.ToString()));

            const string sql =
                "SELECT Host, Username, Name, Password, IsEnabled " +
                "FROM tblSMTPServer WHERE TenantID = @TenantId";

            var row = await connection
                .QuerySingleOrDefaultAsync<(string Host, string Username, string Name, string Password, bool IsEnabled)?>(
                    new CommandDefinition(sql, new { TenantId = tenantId }))
                .ConfigureAwait(false);

            if (!row.HasValue || !row.Value.IsEnabled)
                return null;

            return new SmtpCredential(
                new NetworkCredential(row.Value.Username, Encryptor.DecryptPassword(row.Value.Password)),
                row.Value.Host,
                row.Value.Name ?? DefaultSenderName,
                row.Value.Username);
        }
    }
}
