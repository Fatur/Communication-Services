using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using CommunicationServices.Application.Interfaces;
using CommunicationServices.Domain.Entities;
using CommunicationServices.Application.Exceptions;
using CommunicationServices.Infrastructure.Templates;

namespace CommunicationServices.Application.Handlers
{
    public class EmailHandler : IMessageHandler
    {
        private readonly ITemplateService _templateService;
        private readonly IEmailProvider _emailProvider;
        private readonly IRateLimiter _rateLimiter;
        private readonly ICircuitBreaker _circuitBreaker;
        private readonly ILogger<EmailHandler> _logger;

        public EmailHandler(ITemplateService templateService, IEmailProvider emailProvider, IRateLimiter rateLimiter, ICircuitBreaker circuitBreaker, ILogger<EmailHandler> logger)
        {
            _templateService = templateService;
            _emailProvider = emailProvider;
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
            await _emailProvider.SendAsync(message.Recipient, body);
            _logger.LogInformation("Email sent to {To}", message.Recipient);
        }
    }
}