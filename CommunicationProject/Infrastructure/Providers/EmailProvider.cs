using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CommunicationServices.Application.Interfaces;

namespace CommunicationServices.Infrastructure.Providers
{
    public class EmailProvider : IEmailProvider
    {
        private readonly ILogger<EmailProvider> _logger;
        public EmailProvider(ILogger<EmailProvider> logger)
        {
            _logger = logger;
        }

        public Task SendAsync(string to, string body)
        {
            _logger.LogInformation("[EmailProvider] Sending email to {To}: {Body}", to, body);
            // Simulate success
            return Task.CompletedTask;
        }
    }
}
