using System;
using System.Linq;
using System.Threading;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CommunicationServices.Application.Interfaces;
using CommunicationServices.Application.Handlers;
using CommunicationServices.Domain.Entities;
using CommunicationServices.Application.Exceptions;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace CommunicationServices.Worker
{
    public class MessageProcessorService : BackgroundService
    {
        private readonly ILogger<MessageProcessorService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ICircuitBreaker _circuitBreaker;
        private readonly SemaphoreSlim _semaphore;

        // thread-safe RNG per thread
        private static readonly ThreadLocal<Random> _rng = new ThreadLocal<Random>(() => new Random(RandomNumberGenerator.GetInt32(int.MaxValue)));

        public MessageProcessorService(ILogger<MessageProcessorService> logger, IServiceProvider serviceProvider, ICircuitBreaker circuitBreaker)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _circuitBreaker = circuitBreaker;
            _semaphore = new SemaphoreSlim(5);
        }

        public static class Channels
        {
            public const string Email = "email";
            public const string WhatsApp = "whatsapp";
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Message processor started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Claim batch using a short-lived scope
                    using var claimScope = _serviceProvider.CreateScope();
                    var claimRepo = claimScope.ServiceProvider.GetRequiredService<IMessageRepository>();

                    var batch = (await claimRepo.ClaimPendingAsync(20)).ToList();
                    if (batch.Count == 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                        continue;
                    }

                    var tasks = new List<Task>(batch.Count);
                    foreach (var message in batch)
                    {
                        // start a task per message; each task will create its own scope and repository
                        tasks.Add(ProcessSingleMessageAsync(message, stoppingToken));
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

        private async Task ProcessSingleMessageAsync(MessageLog message, CancellationToken ct)
        {
            await _semaphore.WaitAsync(ct);
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var repository = scope.ServiceProvider.GetRequiredService<IMessageRepository>();
                var rateLimiter = scope.ServiceProvider.GetRequiredService<IRateLimiter>();

                var channel = (message.Channel ?? string.Empty).Trim().ToLowerInvariant();

                // IMPORTANT: ICircuitBreaker must maintain separate state per channel.
                // Use per-channel checks to avoid global circuit state interfering across channels.
                if (_circuitBreaker.IsOpen(channel))
                {
                    var delay = _circuitBreaker.GetRetryDelay(channel) ?? TimeSpan.FromSeconds(60);
                    await RescheduleAsync(repository, message, delay, "circuit open (pre-check)", ct);
                    _logger.LogInformation("Message {MessageId} preemptively rescheduled due to open circuit for tenant {Tenant} channel {Channel}, retry in {Delay}",
                        message.Id, message.TenantId, channel, delay);
                    return;
                }

                // Rate limiter pre-check (keyed by tenant + channel via interface)
                var allowed = await rateLimiter.TryAcquireAsync(message.TenantId, channel);
                if (!allowed)
                {
                    await RescheduleAsync(repository, message, TimeSpan.FromSeconds(5), "rate limited (pre-check)", ct);
                    _logger.LogInformation("Message {MessageId} throttled by rate limiter for tenant {Tenant} channel {Channel}, rescheduled for 5s",
                        message.Id, message.TenantId, channel);
                    return;
                }

                try
                {
                    _logger.LogInformation("Processing message {MessageId} for tenant {Tenant} channel {Channel}", message.Id, message.TenantId, channel);

                    var handler = ResolveHandler(scope.ServiceProvider, channel);

                    await handler.HandleAsync(message);

                    message.Status = "sent";
                    message.SentAt = DateTime.UtcNow;
                    message.ProcessingAt = null;
                    await repository.UpdateAsync(message, ct);

                    _circuitBreaker.OnSuccess(channel);
                    _logger.LogInformation("Message {MessageId} sent successfully for tenant {Tenant} channel {Channel}", message.Id, message.TenantId, channel);
                }
                catch (RateLimitExceededException)
                {
                    await RescheduleAsync(repository, message, TimeSpan.FromSeconds(5), "rate limited", ct);
                    _logger.LogInformation("Message {MessageId} rate limited for tenant {Tenant} channel {Channel}, rescheduled", message.Id, message.TenantId, channel);
                }
                catch (CircuitBreakerOpenException)
                {
                    var delay = _circuitBreaker.GetRetryDelay(channel) ?? TimeSpan.FromSeconds(60);
                    await RescheduleAsync(repository, message, delay, "circuit open", ct);
                    _logger.LogInformation("Message {MessageId} circuit open for tenant {Tenant} channel {Channel}, rescheduled for {Delay}", message.Id, message.TenantId, channel, delay);
                }
                catch (Exception ex)
                {
                    // If the handler resolution failed because channel is unknown, mark permanently failed with a clear message
                    if (ex is InvalidOperationException && ex.Message.StartsWith("Unknown channel", StringComparison.OrdinalIgnoreCase))
                    {
                        message.ErrorMessage = "No handler";
                        message.Status = "failed";
                        message.ProcessingAt = null;
                        await repository.UpdateAsync(message, ct);
                        _logger.LogError(ex, "Message {MessageId} failed due to unknown channel for tenant {Tenant} channel {Channel}", message.Id, message.TenantId, channel);
                        return;
                    }

                    await HandleProcessingFailureAsync(repository, message, channel, ex, ct);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private IMessageHandler ResolveHandler(IServiceProvider provider, string channel)
        {
            // Use non-generic GetService to allow tests to return mocks or other objects
            // that implement IMessageHandler even if they are not the concrete handler type.
            return channel switch
            {
                Channels.Email => provider.GetService(typeof(Application.Handlers.EmailHandler)) as IMessageHandler ?? throw new InvalidOperationException("EmailHandler not registered"),
                Channels.WhatsApp => provider.GetService(typeof(Application.Handlers.WhatsAppHandler)) as IMessageHandler ?? throw new InvalidOperationException("WhatsAppHandler not registered"),
                _ => throw new InvalidOperationException($"Unknown channel '{channel}'")
            };
        }

        private async Task RescheduleAsync(IMessageRepository repository, MessageLog message, TimeSpan delay, string reason, CancellationToken ct)
        {
            message.Status = "pending";
            // increment retry count when rescheduling so retries are tracked
            message.RetryCount++;
            message.NextRetryAt = DateTime.UtcNow.Add(delay);
            message.ProcessingAt = null;
            await repository.UpdateAsync(message, ct);
        }

        private async Task HandleProcessingFailureAsync(IMessageRepository repository, MessageLog message, string channel, Exception ex, CancellationToken ct)
        {
            message.RetryCount++;
            message.ErrorMessage = ex.Message;
            message.ProcessingAt = null;

            if (message.RetryCount > 5)
            {
                message.Status = "failed";
                await repository.UpdateAsync(message, ct);
                _logger.LogError(ex, "Message {MessageId} failed permanently after retries for tenant {Tenant} channel {Channel}", message.Id, message.TenantId, channel);
                _circuitBreaker.OnFailure(channel);
            }
            else
            {
                var backoff = ComputeBackoffDelaySeconds(message.RetryCount);
                var jitter = _rng.Value!.Next(0, 6);
                var delay = TimeSpan.FromSeconds(backoff + jitter);
                message.Status = "pending";
                message.NextRetryAt = DateTime.UtcNow.Add(delay);
                await repository.UpdateAsync(message, ct);
                _logger.LogWarning(ex, "Message {MessageId} failed, will retry in {Delay} (retry #{RetryCount}) for tenant {Tenant} channel {Channel}", message.Id, delay, message.RetryCount, message.TenantId, channel);
                _circuitBreaker.OnFailure(channel);
            }
        }

        private static double ComputeBackoffDelaySeconds(int retryCount)
        {
            // exponential backoff base 2, multiplied by 5s
            return Math.Pow(2, retryCount) * 5;
        }
    }
}
