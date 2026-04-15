using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CommunicationService.Application.Interfaces;

namespace CommunicationService.Infrastructure.Providers
{
    public class WhatsAppProvider : IWhatsAppProvider
    {
        private readonly ILogger<WhatsAppProvider> _logger;
        public WhatsAppProvider(ILogger<WhatsAppProvider> logger)
        {
            _logger = logger;
        }

        public Task SendAsync(string to, string body)
        {
            _logger.LogInformation("[WhatsAppProvider] Sending whatsapp to {To}: {Body}", to, body);
            // Simulate success
            return Task.CompletedTask;
        }
    }
}
