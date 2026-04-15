using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CommunicationService.Application.Interfaces;
using CommunicationService.Application.Handlers;
using CommunicationService.Domain.Entities;
using CommunicationService.Application.Exceptions;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace CommunicationService.Worker
{
    public class MessageProcessorService : BackgroundService
    {
        private readonly ILogger<MessageProcessorService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly EmailHandler _emailHandler;
        private readonly WhatsAppHandler _whatsAppHandler;
        private readonly ICircuitBreaker _circuitBreaker;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(5);
        private readonly Random _random = new Random();

        public MessageProcessorService(ILogger<MessageProcessorService> logger, IServiceProvider serviceProvider, EmailHandler emailHandler, WhatsAppHandler whatsAppHandler, ICircuitBreaker circuitBreaker)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _emailHandler = emailHandler;
            _whatsAppHandler = whatsAppHandler;
            _circuitBreaker = circuitBreaker;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Message processor started");
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var repository = scope.ServiceProvider.GetRequiredService<IMessageRepository>();

                    var batch = (await repository.ClaimPendingAsync(20)).ToList();
                    if (!batch.Any())
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                        continue;
                    }

                    var tasks = new List<Task>();
                    foreach (var message in batch)
                    {
                        await _semaphore.WaitAsync(stoppingToken);
                        tasks.Add(ProcessMessageAsync(message, repository, stoppingToken).ContinueWith(t => _semaphore.Release()));
                    }

                    await Task.WhenAll(tasks);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in processor loop");
                }

                // small delay between batches
                await Task.Delay(200, stoppingToken);
            }
        }

        private async Task ProcessMessageAsync(MessageLog message, IMessageRepository repository, CancellationToken ct)
        {
            var channel = message.Channel.ToLowerInvariant();
            try
            {
                if (channel == "email")
                {
                    await _emailHandler.HandleAsync(message);
                }
                else if (channel == "whatsapp")
                {
                    await _whatsAppHandler.HandleAsync(message);
                }
                else
                {
                    throw new Exception($"Unknown channel {message.Channel}");
                }

                message.Status = "sent";
                message.SentAt = DateTime.UtcNow;
                message.ProcessingAt = null;
                await repository.UpdateAsync(message);
                _circuitBreaker.OnSuccess(channel);
                _logger.LogInformation("Message {Id} sent", message.Id);
            }
            catch (RateLimitExceededException)
            {
                // reschedule in 5 seconds
                message.Status = "pending";
                message.NextRetryAt = DateTime.UtcNow.AddSeconds(5);
                message.ProcessingAt = null;
                await repository.UpdateAsync(message);
                _logger.LogInformation("Message {Id} rate limited, rescheduled", message.Id);
            }
            catch (CircuitBreakerOpenException)
            {
                var delay = _circuitBreaker.GetRetryDelay(channel) ?? TimeSpan.FromSeconds(60);
                message.Status = "pending";
                message.NextRetryAt = DateTime.UtcNow.Add(delay);
                message.ProcessingAt = null;
                await repository.UpdateAsync(message);
                _logger.LogInformation("Message {Id} circuit open, rescheduled for {Delay}", message.Id, delay);
            }
            catch (Exception ex)
            {
                // General failure: retry with exponential backoff
                message.RetryCount++;
                message.ErrorMessage = ex.Message;
                message.ProcessingAt = null;

                if (message.RetryCount > 5)
                {
                    message.Status = "failed";
                    await repository.UpdateAsync(message);
                    _logger.LogError(ex, "Message {Id} failed permanently after retries", message.Id);
                    _circuitBreaker.OnFailure(channel);
                }
                else
                {
                    var backoffSeconds = Math.Pow(2, message.RetryCount) * 5;
                    var jitter = _random.Next(0, 6);
                    var delay = TimeSpan.FromSeconds(backoffSeconds + jitter);
                    message.Status = "pending";
                    message.NextRetryAt = DateTime.UtcNow.Add(delay);
                    await repository.UpdateAsync(message);
                    _logger.LogWarning(ex, "Message {Id} failed, will retry in {Delay}. Retry #{RetryCount}", message.Id, delay, message.RetryCount);
                    _circuitBreaker.OnFailure(channel);
                }
            }
        }
    }
}
