using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunicationServices.Domain.Entities;
using CommunicationServices.Infrastructure.Enum;
using CommunicationServices.Infrastructure.Providers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CommunicationServices.Tests.Integration
{
    /// <summary>
    /// Integration tests for SmtpEmailProvider.
    /// 
    /// Konfigurasi dibaca dari appsettings.json (section "Smtp").
    /// Environment variables (SMTP_HOST, dll.) bisa override jika di-set.
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
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.test.json", optional: false)
                .AddEnvironmentVariables()
                .Build();

            _toEmail = config["Smtp:SMTP_TO_EMAIL"] ?? "";
            _provider = new SmtpEmailProvider(config, NullLogger<SmtpEmailProvider>.Instance);
        }

        private MessageLog CreateMessageLog(string recipient)
        {
            return new MessageLog
            {
                Id = Guid.NewGuid(),
                Recipient = recipient,
                Recipients = new List<string> { recipient },
                Channel = "email",
                CreatedAt = DateTime.UtcNow
            };
        }

        [Fact]
        public async Task SendAsync_WithValidEmail_ShouldSucceed()
        {
            // Arrange
            var message = CreateMessageLog(_toEmail);

            // Act & Assert: tidak boleh throw exception
            await _provider.SendAsync(Requestor.Soficloud, message, "Ini adalah email test dari integration test.");
        }

        [Fact]
        public async Task SendAsync_WithInvalidRecipient_ShouldThrowException()
        {
            // Arrange
            var invalidEmail = "bukan-email-valid";
            var message = CreateMessageLog(invalidEmail);

            // Act & Assert
            await Assert.ThrowsAnyAsync<Exception>(() =>
                _provider.SendAsync(Requestor.Soficloud, message, "Test body"));
        }

        [Fact]
        public async Task SendAsync_WithEmptyBody_ShouldSucceed()
        {
            // Arrange
            var message = CreateMessageLog(_toEmail);

            // Act & Assert: body kosong tetap harus bisa terkirim
            await _provider.SendAsync(Requestor.Soficloud, message, string.Empty);
        }
    }
}
