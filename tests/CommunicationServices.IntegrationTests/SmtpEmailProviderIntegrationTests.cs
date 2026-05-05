using System;
using System.Threading.Tasks;
using CommunicationServices.Infrastructure.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CommunicationServices.Tests.Integration
{
    /// <summary>
    /// Integration tests for SmtpEmailProvider.
    /// 
    /// SETUP: Sebelum menjalankan test ini, set environment variables berikut:
    ///   SMTP_HOST         = smtp server (contoh: smtp.gmail.com)
    ///   SMTP_PORT         = port (contoh: 587)
    ///   SMTP_USERNAME     = username/email akun pengirim
    ///   SMTP_PASSWORD     = password atau app password
    ///   SMTP_SENDER_EMAIL = alamat email pengirim
    ///   SMTP_SENDER_NAME  = nama pengirim (contoh: Test App)
    ///   SMTP_TO_EMAIL     = alamat email penerima untuk test
    /// 
    /// Jalankan hanya integration test:
    ///   dotnet test --filter "Category=Integration"
    /// </summary>
    [Trait("Category", "Integration")]
    public class SmtpEmailProviderIntegrationTests
    {
        private readonly SmtpEmailProvider _provider;
        private readonly string _toEmail;

        public SmtpEmailProviderIntegrationTests()
        {
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new[]
                {
                    new System.Collections.Generic.KeyValuePair<string, string>("Smtp:Host",         Environment.GetEnvironmentVariable("SMTP_HOST") ?? ""),
                    new System.Collections.Generic.KeyValuePair<string, string>("Smtp:Port",         Environment.GetEnvironmentVariable("SMTP_PORT") ?? "587"),
                    new System.Collections.Generic.KeyValuePair<string, string>("Smtp:Username",     Environment.GetEnvironmentVariable("SMTP_USERNAME") ?? ""),
                    new System.Collections.Generic.KeyValuePair<string, string>("Smtp:Password",     Environment.GetEnvironmentVariable("SMTP_PASSWORD") ?? ""),
                    new System.Collections.Generic.KeyValuePair<string, string>("Smtp:SenderEmail",  Environment.GetEnvironmentVariable("SMTP_SENDER_EMAIL") ?? ""),
                    new System.Collections.Generic.KeyValuePair<string, string>("Smtp:SenderName",   Environment.GetEnvironmentVariable("SMTP_SENDER_NAME") ?? "Test App"),
                    new System.Collections.Generic.KeyValuePair<string, string>("Smtp:DefaultSubject", "Integration Test Email"),
                    new System.Collections.Generic.KeyValuePair<string, string>("Smtp:EnableSsl",   "true"),
                })
                .Build();

            _toEmail = Environment.GetEnvironmentVariable("SMTP_TO_EMAIL") ?? "";
            _provider = new SmtpEmailProvider(config, NullLogger<SmtpEmailProvider>.Instance);
        }

        [Fact]
        public async Task SendAsync_WithValidEmail_ShouldSucceed()
        {
            // Act & Assert: tidak boleh throw exception
            await _provider.SendAsync(_toEmail, "Ini adalah email test dari integration test.");
        }

        [Fact]
        public async Task SendAsync_WithInvalidRecipient_ShouldThrowException()
        {
            // Arrange
            var invalidEmail = "bukan-email-valid";

            // Act & Assert
            await Assert.ThrowsAnyAsync<Exception>(() =>
                _provider.SendAsync(invalidEmail, "Test body"));
        }

        [Fact]
        public async Task SendAsync_WithEmptyBody_ShouldSucceed()
        {
            // Act & Assert: body kosong tetap harus bisa terkirim
            await _provider.SendAsync(_toEmail, string.Empty);
        }
    }
}
