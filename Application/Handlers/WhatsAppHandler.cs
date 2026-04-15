using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CommunicationService.Application.Interfaces;
using CommunicationService.Domain.Entities;
using CommunicationService.Application.Exceptions;
using CommunicationService.Infrastructure.Templates;

namespace CommunicationService.Application.Handlers
{
    public class WhatsAppHandler : IMessageHandler
    {
        private readonly ITemplateService _templateService;
        private readonly IWhatsAppProvider _whatsAppProvider;
        private readonly IRateLimiter _rateLimiter;
        private readonly ICircuitBreaker _circuitBreaker;
        private readonly ILogger<WhatsAppHandler> _logger;

        public WhatsAppHandler(ITemplateService templateService, IWhatsAppProvider whatsAppProvider, IRateLimiter rateLimiter, ICircuitBreaker circuitBreaker, ILogger<WhatsAppHandler> logger)
        {
            _templateService = templateService;
            _whatsAppProvider = whatsAppProvider;
            _rateLimiter = rateLimiter;
            _circuitBreaker = circuitBreaker;
            _logger = logger;
        }

        public async Task HandleAsync(MessageLog message)
        {
            var channel = message.Channel.ToLowerInvariant();
            if (_circuitBreaker.IsOpen(channel))
            {
                _logger.LogWarning("Circuit is open for channel {Channel}", channel);
                throw new CircuitBreakerOpenException("Circuit is open");
            }

            var allowed = await _rateLimiter.TryAcquireAsync(message.TenantId, channel);
            if (!allowed)
            {
                _logger.LogWarning("Rate limit exceeded for tenant {Tenant} channel {Channel}", message.TenantId, channel);
                throw new RateLimitExceededException("Rate limit exceeded");
            }

            var body = await _templateService.RenderAsync(message.TemplateCode, message.DataJson);
            await _whatsAppProvider.SendAsync(message.Recipient, body);
            _logger.LogInformation("WhatsApp sent to {To}", message.Recipient);
        }
    }
}