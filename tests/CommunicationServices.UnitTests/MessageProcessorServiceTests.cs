using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

using CommunicationServices.Worker;
using CommunicationServices.Application.Interfaces;
using CommunicationServices.Domain.Entities;
using CommunicationServices.Application.Handlers;
using CommunicationServices.Application.Exceptions;

namespace CommunicationServices.UnitTests
{
    // Tests target the real MessageProcessorService in CommunicationServices.Worker
    public class MessageProcessorServiceTests
    {
        private readonly Mock<IMessageRepository> _repo = new();
        private readonly Mock<IRateLimiter> _rateLimiter = new();
        private readonly Mock<ICircuitBreaker> _circuit = new();
        private readonly Mock<IMessageHandler> _emailHandler = new();
        private readonly Mock<IMessageHandler> _whatsappHandler = new();
        private readonly Mock<CommunicationServices.Infrastructure.Templates.ITemplateService> _templateService = new();
        private readonly Mock<CommunicationServices.Application.Interfaces.IEmailProvider> _emailProvider = new();
        private readonly Mock<CommunicationServices.Application.Interfaces.IWhatsAppProvider> _whatsAppProvider = new();
        private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
        private readonly Mock<IServiceScope> _scope = new();
        private readonly Mock<IServiceProvider> _scopeServiceProvider = new();
        private readonly Mock<IServiceProvider> _rootServiceProvider = new();
        private readonly Mock<ILogger<MessageProcessorService>> _logger = new();

        private MessageProcessorService CreateService()
        {
            // MessageProcessorService constructor: (ILogger<MessageProcessorService>, IServiceProvider, ICircuitBreaker)
            return new MessageProcessorService(_logger.Object, _rootServiceProvider.Object, _circuit.Object);
        }

        private void SetupRootScope()
        {
            // Configure root provider to return our scope factory
            _rootServiceProvider.Setup(r => r.GetService(typeof(IServiceScopeFactory))).Returns(_scopeFactory.Object);
            _scopeFactory.Setup(f => f.CreateScope()).Returns(_scope.Object);
            _scope.SetupGet(s => s.ServiceProvider).Returns(_scopeServiceProvider.Object);

            // Scoped service provider should return repository and rate limiter and handlers
            _scopeServiceProvider.Setup(sp => sp.GetService(typeof(IMessageRepository))).Returns(_repo.Object);
            _scopeServiceProvider.Setup(sp => sp.GetService(typeof(IRateLimiter))).Returns(_rateLimiter.Object);

            // Scoped service provider should return repository and rate limiter and handlers
            _scopeServiceProvider.Setup(sp => sp.GetService(typeof(IMessageRepository))).Returns(_repo.Object);
            _scopeServiceProvider.Setup(sp => sp.GetService(typeof(IRateLimiter))).Returns(_rateLimiter.Object);
            // When ResolveHandler uses GetRequiredService<Application.Handlers.EmailHandler>() it will call GetService(typeof(EmailHandler)) under the hood
            _scopeServiceProvider.Setup(sp => sp.GetService(typeof(EmailHandler))).Returns(_emailHandler.Object);
            _scopeServiceProvider.Setup(sp => sp.GetService(typeof(WhatsAppHandler))).Returns(_whatsappHandler.Object);
        }

        // Helper to setup ClaimPendingAsync returning given messages
        private void SetupClaim(params MessageLog[] messages)
        {
            // Make ClaimPendingAsync return the provided messages once, then empty on subsequent calls
            var seq = _repo.SetupSequence(r => r.ClaimPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()));
            if (messages != null && messages.Length > 0)
            {
                seq = seq.ReturnsAsync((IEnumerable<MessageLog>)messages);
            }
            else
            {
                seq = seq.ReturnsAsync(Array.Empty<MessageLog>());
            }

            // always return empty after the first
            seq.ReturnsAsync(Array.Empty<MessageLog>());

            _repo.Setup(r => r.UpdateAsync(It.IsAny<MessageLog>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        }

        // 1. CLAIMING & PROCESSING
        [Fact]
        public async Task Should_claim_pending_messages()
        {
            // Arrange
            SetupRootScope();
            var msg = new MessageLog { Id = Guid.NewGuid(), Channel = "email", TenantId = "t1", Status = "pending" };
            SetupClaim(msg);
            _rateLimiter.Setup(r => r.TryAcquireAsync(msg.TenantId, msg.Channel)).ReturnsAsync(true);
            _circuit.Setup(c => c.IsOpen(It.IsAny<string>())).Returns(false);
            _circuit.Setup(c => c.GetRetryDelay(It.IsAny<string>())).Returns((TimeSpan?)null);
            _emailHandler.Setup(h => h.HandleAsync(It.IsAny<MessageLog>())).Returns(Task.CompletedTask);

            var svc = CreateService();

            // Act: call protected ExecuteAsync via reflection and cancel shortly after
            var execute = typeof(MessageProcessorService).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var cts = new CancellationTokenSource(2000);
            try
            {
                await (Task)execute.Invoke(svc, new object[] { cts.Token })!;
            }
            catch (OperationCanceledException) { }

            // Assert
            _repo.Verify(r => r.ClaimPendingAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
            _emailHandler.Verify(h => h.HandleAsync(It.Is<MessageLog>(m => m.Id == msg.Id)), Times.Once);
            _repo.Verify(r => r.UpdateAsync(It.Is<MessageLog>(m => m.Status == "sent" && m.SentAt != null), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task Should_not_process_when_no_messages()
        {
            // Arrange
            SetupRootScope();
            SetupClaim(); // no messages

            var svc = CreateService();
            var execute = typeof(MessageProcessorService).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var cts = new CancellationTokenSource(1000);

            // Act
            try
            {
                await (Task)execute.Invoke(svc, new object[] { cts.Token })!;
            }
            catch (OperationCanceledException) { }

            // Assert
            _emailHandler.Verify(h => h.HandleAsync(It.IsAny<MessageLog>()), Times.Never);
            _repo.Verify(r => r.UpdateAsync(It.IsAny<MessageLog>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task Should_process_multiple_messages()
        {
            // Arrange
            SetupRootScope();
            var m1 = new MessageLog { Id = Guid.NewGuid(), Channel = "email", TenantId = "t" , Status = "pending"};
            var m2 = new MessageLog { Id = Guid.NewGuid(), Channel = "email", TenantId = "t" , Status = "pending"};
            var m3 = new MessageLog { Id = Guid.NewGuid(), Channel = "whatsapp", TenantId = "t" , Status = "pending"};
            SetupClaim(m1, m2, m3);
            _rateLimiter.Setup(r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _circuit.Setup(c => c.IsOpen(It.IsAny<string>())).Returns(false);
            _emailHandler.Setup(h => h.HandleAsync(It.IsAny<MessageLog>())).Returns(Task.CompletedTask);
            _whatsappHandler.Setup(h => h.HandleAsync(It.IsAny<MessageLog>())).Returns(Task.CompletedTask);

            var svc = CreateService();
            var execute = typeof(MessageProcessorService).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var cts = new CancellationTokenSource(2000);

            // Act
            try
            {
                await (Task)execute.Invoke(svc, new object[] { cts.Token })!;
            }
            catch (OperationCanceledException) { }

            // Assert
            _emailHandler.Verify(h => h.HandleAsync(It.IsAny<MessageLog>()), Times.Exactly(2));
            _whatsappHandler.Verify(h => h.HandleAsync(It.IsAny<MessageLog>()), Times.Exactly(1));
            _repo.Verify(r => r.UpdateAsync(It.Is<MessageLog>(m => m.Status == "sent"), It.IsAny<CancellationToken>()), Times.Exactly(3));
        }

        // 2. SUCCESS FLOW

        [Fact]
        public async Task Should_mark_message_as_sent_on_success()
        {
            // Arrange
            SetupRootScope();
            var msg = new MessageLog { Id = Guid.NewGuid(), Channel = "email", TenantId = "t1", Status = "pending" };
            SetupClaim(msg);
            _rateLimiter.Setup(r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _circuit.Setup(c => c.IsOpen(It.IsAny<string>())).Returns(false);
            _emailHandler.Setup(h => h.HandleAsync(It.IsAny<MessageLog>())).Returns(Task.CompletedTask);

            var svc = CreateService();
            var execute = typeof(MessageProcessorService).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var cts = new CancellationTokenSource(1500);

            // Act
            try
            {
                await (Task)execute.Invoke(svc, new object[] { cts.Token })!;
            }
            catch (OperationCanceledException) { }

            // Assert
            _repo.Verify(r => r.UpdateAsync(It.Is<MessageLog>(m => m.Status == "sent"), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task Should_set_sent_at_timestamp()
        {
            // Arrange
            SetupRootScope();
            var msg = new MessageLog { Id = Guid.NewGuid(), Channel = "email", TenantId = "t1", Status = "pending" };
            SetupClaim(msg);
            _rateLimiter.Setup(r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _circuit.Setup(c => c.IsOpen(It.IsAny<string>())).Returns(false);
            _emailHandler.Setup(h => h.HandleAsync(It.IsAny<MessageLog>())).Returns(Task.CompletedTask);

            var svc = CreateService();
            var execute = typeof(MessageProcessorService).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var cts = new CancellationTokenSource(1500);
            var before = DateTime.UtcNow;

            // Act
            try
            {
                await (Task)execute.Invoke(svc, new object[] { cts.Token })!;
            }
            catch (OperationCanceledException) { }

            // Assert
            _repo.Verify(r => r.UpdateAsync(It.Is<MessageLog>(m => m.SentAt >= before), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }

        [Fact]
        public async Task Should_call_circuit_breaker_on_success()
        {
            // Arrange
            SetupRootScope();
            var msg = new MessageLog { Id = Guid.NewGuid(), Channel = "email", TenantId = "t1", Status = "pending" };
            SetupClaim(msg);
            _rateLimiter.Setup(r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _circuit.Setup(c => c.IsOpen(It.IsAny<string>())).Returns(false);
            _emailHandler.Setup(h => h.HandleAsync(It.IsAny<MessageLog>())).Returns(Task.CompletedTask);

            var svc = CreateService();
            var execute = typeof(MessageProcessorService).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var cts = new CancellationTokenSource(1500);

            // Act
            try
            {
                await (Task)execute.Invoke(svc, new object[] { cts.Token })!;
            }
            catch (OperationCanceledException) { }

            // Assert
            _circuit.Verify(c => c.OnSuccess(It.IsAny<string>()), Times.Once);
            _circuit.Verify(c => c.OnFailure(It.IsAny<string>()), Times.Never);
        }

        // 3. HANDLER RESOLUTION

        [Fact]
        public async Task Should_use_email_handler_for_email_channel()
        {
            // Arrange
            SetupRootScope();
            var msg = new MessageLog { Id = Guid.NewGuid(), Channel = "email", TenantId = "t1", Status = "pending" };
            SetupClaim(msg);
            _rateLimiter.Setup(r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _circuit.Setup(c => c.IsOpen(It.IsAny<string>())).Returns(false);
            _emailHandler.Setup(h => h.HandleAsync(It.IsAny<MessageLog>())).Returns(Task.CompletedTask);

            var svc = CreateService();
            var execute = typeof(MessageProcessorService).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var cts = new CancellationTokenSource(1500);

            // Act
            try
            {
                await (Task)execute.Invoke(svc, new object[] { cts.Token })!;
            }
            catch (OperationCanceledException) { }

            // Assert
            _emailHandler.Verify(h => h.HandleAsync(It.Is<MessageLog>(m => m.Id == msg.Id)), Times.Once);
        }

        [Fact]
        public async Task Should_use_whatsapp_handler_for_whatsapp_channel()
        {
            // Arrange
            SetupRootScope();
            var msg = new MessageLog { Id = Guid.NewGuid(), Channel = "whatsapp", TenantId = "t1", Status = "pending" };
            SetupClaim(msg);
            _rateLimiter.Setup(r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _circuit.Setup(c => c.IsOpen(It.IsAny<string>())).Returns(false);
            _whatsappHandler.Setup(h => h.HandleAsync(It.IsAny<MessageLog>())).Returns(Task.CompletedTask);

            var svc = CreateService();
            var execute = typeof(MessageProcessorService).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var cts = new CancellationTokenSource(1500);

            // Act
            try
            {
                await (Task)execute.Invoke(svc, new object[] { cts.Token })!;
            }
            catch (OperationCanceledException) { }

            // Assert
            _whatsappHandler.Verify(h => h.HandleAsync(It.Is<MessageLog>(m => m.Id == msg.Id)), Times.Once);
            _emailHandler.Verify(h => h.HandleAsync(It.IsAny<MessageLog>()), Times.Never);
        }

        [Fact]
        public async Task Should_throw_for_unknown_channel()
        {
            // Arrange
            SetupRootScope();
            var msg = new MessageLog { Id = Guid.NewGuid(), Channel = "unknown", TenantId = "t1", Status = "pending" };
            SetupClaim(msg);
            _rateLimiter.Setup(r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _circuit.Setup(c => c.IsOpen(It.IsAny<string>())).Returns(false);

            var svc = CreateService();
            var execute = typeof(MessageProcessorService).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var cts = new CancellationTokenSource(1500);

            // Act
            try
            {
                await (Task)execute.Invoke(svc, new object[] { cts.Token })!;
            }
            catch (OperationCanceledException) { }

            // Assert
            _repo.Verify(r => r.UpdateAsync(It.Is<MessageLog>(m => m.Status == "failed" && m.ErrorMessage.Contains("No handler")), It.IsAny<CancellationToken>()), Times.Once);
        }

        // 4. RATE LIMITING

        [Fact]
        public async Task Should_reschedule_when_rate_limiter_blocks()
        {
            // Arrange
            SetupRootScope();
            var msg = new MessageLog { Id = Guid.NewGuid(), Channel = "email", TenantId = "t1", Status = "pending", RetryCount = 0 };
            SetupClaim(msg);
            _rateLimiter.Setup(r => r.TryAcquireAsync("t1", "email")).ReturnsAsync(false);

            var svc = CreateService();
            var execute = typeof(MessageProcessorService).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var cts = new CancellationTokenSource(1000);

            // Act
            try
            {
                await (Task)execute.Invoke(svc, new object[] { cts.Token })!;
            }
            catch (OperationCanceledException) { }

            // Assert
            _emailHandler.Verify(h => h.HandleAsync(It.IsAny<MessageLog>()), Times.Never);
            _repo.Verify(r => r.UpdateAsync(It.Is<MessageLog>(m => m.RetryCount == 1 && m.NextRetryAt != null && m.ProcessingAt == null), It.IsAny<CancellationToken>()), Times.Once);
            _circuit.Verify(c => c.OnFailure(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Should_not_call_handler_when_rate_limited()
        {
            // Arrange
            SetupRootScope();
            var msg = new MessageLog { Id = Guid.NewGuid(), Channel = "email", TenantId = "t1", Status = "pending" };
            SetupClaim(msg);
            _rateLimiter.Setup(r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

            var svc = CreateService();
            var execute = typeof(MessageProcessorService).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var cts = new CancellationTokenSource(1500);

            // Act
            try
            {
                await (Task)execute.Invoke(svc, new object[] { cts.Token })!;
            }
            catch (OperationCanceledException) { }

            // Assert
            _emailHandler.Verify(h => h.HandleAsync(It.IsAny<MessageLog>()), Times.Never);
        }

        // 5. CIRCUIT BREAKER

        [Fact]
        public async Task Should_reschedule_when_circuit_is_open()
        {
            // Arrange
            SetupRootScope();
            var msg = new MessageLog { Id = Guid.NewGuid(), Channel = "email", TenantId = "t1", Status = "pending", RetryCount = 0 };
            SetupClaim(msg);
            _rateLimiter.Setup(r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _circuit.Setup(c => c.IsOpen(It.IsAny<string>())).Returns(true);

            var svc = CreateService();
            var execute = typeof(MessageProcessorService).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var cts = new CancellationTokenSource(1000);

            // Act
            await (Task)execute.Invoke(svc, new object[] { cts.Token })!;

            // Assert
            _emailHandler.Verify(h => h.HandleAsync(It.IsAny<MessageLog>()), Times.Never);
            _repo.Verify(r => r.UpdateAsync(It.Is<MessageLog>(m => m.RetryCount == 1 && m.NextRetryAt != null), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task Should_not_call_handler_when_circuit_open()
        {
            // Arrange
            SetupRootScope();
            var msg = new MessageLog { Id = Guid.NewGuid(), Channel = "email", TenantId = "t1", Status = "pending" };
            SetupClaim(msg);
            _rateLimiter.Setup(r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _circuit.Setup(c => c.IsOpen(It.IsAny<string>())).Returns(true);

            var svc = CreateService();
            var execute = typeof(MessageProcessorService).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var cts = new CancellationTokenSource(1000);

            // Act
            try
            {
                await (Task)execute.Invoke(svc, new object[] { cts.Token })!;
            }
            catch (OperationCanceledException) { }

            // Assert
            _emailHandler.Verify(h => h.HandleAsync(It.IsAny<MessageLog>()), Times.Never);
        }

        [Fact]
        public async Task Should_call_circuit_breaker_on_failure()
        {
            // Arrange
            SetupRootScope();
            var msg = new MessageLog { Id = Guid.NewGuid(), Channel = "email", TenantId = "t1", Status = "pending" };
            SetupClaim(msg);
            _rateLimiter.Setup(r => r.TryAcquireAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _circuit.Setup(c => c.IsOpen(It.IsAny<string>())).Returns(false);
            _emailHandler.Setup(h => h.HandleAsync(It.IsAny<MessageLog>())).ThrowsAsync(new Exception("boom"));

            var svc = CreateService();
            var execute = typeof(MessageProcessorService).GetMethod("ExecuteAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            using var cts = new CancellationTokenSource(1500);

            // Act
            await (Task)execute.Invoke(svc, new object[] { cts.Token })!;

            // Assert
            _circuit.Verify(c => c.OnSuccess(It.IsAny<string>()), Times.Never);
            _circuit.Verify(c => c.OnFailure(It.IsAny<string>()), Times.Once);
        }
    }
}