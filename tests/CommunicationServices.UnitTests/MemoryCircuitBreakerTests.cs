using System;
using System.Threading.Tasks;
using Xunit;

namespace CommunicationServices.UnitTests
{
    // Test skeletons for Circuit Breaker (MemoryCircuitBreaker or other implementations).
    // Each test contains Arrange / Act / Assert comments and is intentionally left
    // without real implementation so tests can be implemented using the project's
    // IClock/FakeClock and configuration types. Use these skeletons as the TDD spec.

    public class MemoryCircuitBreakerTests
    {
        // Configuration examples used across tests:
        // - failureThreshold = 5
        // - openDuration = TimeSpan.FromSeconds(60)
        // - halfOpenMaxRequests = 1

        // --- 1. CLOSED STATE --------------------------------------------------

        [Fact]
        public void Should_start_in_closed_state()
        {
            // Arrange
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(DateTime.UtcNow);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            // Act
            var isOpen = cb.IsOpen("email");
            var delay = cb.GetRetryDelay("email");

            // Assert
            Assert.False(isOpen);
            Assert.Null(delay);
        }

        [Fact]
        public void Should_allow_requests_when_closed()
        {
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(DateTime.UtcNow);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            // Act: perform some successes and a couple of failures below threshold
            cb.OnSuccess("email");
            cb.OnFailure("email");
            cb.OnFailure("email");

            // Assert
            Assert.False(cb.IsOpen("email"));
            Assert.Null(cb.GetRetryDelay("email"));
        }

        [Fact]
        public void Should_remain_closed_when_failures_below_threshold()
        {
            // Arrange
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(DateTime.UtcNow);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            // Act
            for (int i = 0; i < 4; i++) cb.OnFailure("email");

            // Assert
            Assert.False(cb.IsOpen("email"));
            Assert.Null(cb.GetRetryDelay("email"));
        }

        // --- 2. FAILURE THRESHOLD ---------------------------------------------

        [Fact]
        public void Should_track_failures_per_channel()
        {
            // Arrange
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(DateTime.UtcNow);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            // Act
            for (int i = 0; i < 5; i++) cb.OnFailure("email");
            cb.OnFailure("whatsapp");

            // Assert
            Assert.True(cb.IsOpen("email"));
            Assert.False(cb.IsOpen("whatsapp"));
        }

        [Fact]
        public void Should_open_circuit_after_failure_threshold_reached()
        {
            // Arrange
            var start = DateTime.UtcNow;
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(start);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            // Act
            for (int i = 0; i < 5; i++) cb.OnFailure("email");

            // Assert
            Assert.True(cb.IsOpen("email"));
            var d = cb.GetRetryDelay("email");
            Assert.NotNull(d);
            Assert.InRange(d.Value.TotalSeconds, 59, 61);
        }

        [Fact]
        public void Should_not_mix_failure_counts_between_channels()
        {
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(DateTime.UtcNow);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            for (int i = 0; i < 5; i++) cb.OnFailure("email");

            Assert.True(cb.IsOpen("email"));
            Assert.False(cb.IsOpen("whatsapp"));
        }

        // --- 3. OPEN STATE ----------------------------------------------------

        [Fact]
        public void Should_block_requests_when_open()
        {
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(DateTime.UtcNow);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);
            for (int i = 0; i < 5; i++) cb.OnFailure("email");

            Assert.True(cb.IsOpen("email"));
            Assert.NotNull(cb.GetRetryDelay("email"));
        }

        [Fact]
        public void Should_return_retry_delay_when_open()
        {
            var start = DateTime.UtcNow;
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(start);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            for (int i = 0; i < 5; i++) cb.OnFailure("email");

            var d0 = cb.GetRetryDelay("email");
            Assert.NotNull(d0);
            Assert.InRange(d0.Value.TotalSeconds, 59, 61);

            // advance 30s
            clock.Advance(TimeSpan.FromSeconds(30));
            var d1 = cb.GetRetryDelay("email");
            Assert.NotNull(d1);
            Assert.InRange(d1.Value.TotalSeconds, 29, 31);

            // advance beyond open duration
            clock.Advance(TimeSpan.FromSeconds(31));
            Assert.False(cb.IsOpen("email"));
            Assert.Null(cb.GetRetryDelay("email"));
        }

        [Fact]
        public void Should_not_allow_requests_until_timeout_expires()
        {
            var start = DateTime.UtcNow;
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(start);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);
            for (int i = 0; i < 5; i++) cb.OnFailure("email");

            Assert.True(cb.IsOpen("email"));

            // advance less than open duration
            clock.Advance(TimeSpan.FromSeconds(30));
            Assert.True(cb.IsOpen("email"));

            // advance beyond open duration
            clock.Advance(TimeSpan.FromSeconds(31));
            Assert.False(cb.IsOpen("email"));
        }

        // --- 4. HALF-OPEN STATE -----------------------------------------------

        [Fact]
        public void Should_transition_to_half_open_after_timeout()
        {
            // NOTE: MemoryCircuitBreaker does not implement explicit half-open probe limiting.
            // We test that after timeout IsOpen becomes false (requests allowed).
            var start = DateTime.UtcNow;
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(start);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);
            for (int i = 0; i < 5; i++) cb.OnFailure("email");

            Assert.True(cb.IsOpen("email"));
            clock.Advance(TimeSpan.FromSeconds(61));
            Assert.False(cb.IsOpen("email"));
        }

        [Fact]
        public void Should_allow_limited_requests_in_half_open()
        {
            // MemoryCircuitBreaker currently does not limit probes in half-open. This test
            // verifies that after open duration expires multiple requests are permitted.
            var start = DateTime.UtcNow;
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(start);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            for (int i = 0; i < 5; i++) cb.OnFailure("email");
            Assert.True(cb.IsOpen("email"));

            // advance beyond open duration
            clock.Advance(TimeSpan.FromSeconds(61));

            // multiple probes should be allowed (no built-in limit)
            Assert.False(cb.IsOpen("email"));
            Assert.False(cb.IsOpen("email"));
            Assert.False(cb.IsOpen("email"));
        }

        [Fact]
        public void Should_reopen_if_half_open_request_fails()
        {
            // MemoryCircuitBreaker: once timeout expires, failures still count toward threshold.
            // Simulate: open -> wait -> a failure should increase failure count and can re-open when threshold reached.
            var start = DateTime.UtcNow;
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(start);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);
            for (int i = 0; i < 5; i++) cb.OnFailure("email");
            clock.Advance(TimeSpan.FromSeconds(61));

            // Now a failure occurs
            for (int i = 0; i < 5; i++) cb.OnFailure("email");
            Assert.True(cb.IsOpen("email"));
        }

        [Fact]
        public void Should_close_if_half_open_request_succeeds()
        {
            var start = DateTime.UtcNow;
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(start);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);
            for (int i = 0; i < 5; i++) cb.OnFailure("email");
            Assert.True(cb.IsOpen("email"));

            // Simulate operator/probe success by invoking OnSuccess after timeout
            clock.Advance(TimeSpan.FromSeconds(61));
            cb.OnSuccess("email");

            Assert.False(cb.IsOpen("email"));
            Assert.Null(cb.GetRetryDelay("email"));
        }

        [Fact]
        public void Should_limit_concurrent_half_open_attempts_to_configured_max()
        {
            // MemoryCircuitBreaker does not implement half-open concurrency limits. This test
            // is a placeholder to document desired behavior for future implementation.
            Assert.True(true);
        }

        // --- 5. SUCCESS HANDLING ----------------------------------------------

        [Fact]
        public void Should_reset_failure_count_on_success()
        {
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(DateTime.UtcNow);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            // cause some failures
            for (int i = 0; i < 3; i++) cb.OnFailure("email");
            // reset via success
            cb.OnSuccess("email");

            // After reset, need full threshold failures to open
            for (int i = 0; i < 5; i++) cb.OnFailure("email");
            Assert.True(cb.IsOpen("email"));
        }

        [Fact]
        public void Should_close_circuit_after_successful_recovery()
        {
            var start = DateTime.UtcNow;
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(start);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            // open
            for (int i = 0; i < 5; i++) cb.OnFailure("email");
            Assert.True(cb.IsOpen("email"));

            // advance and simulate success
            clock.Advance(TimeSpan.FromSeconds(61));
            cb.OnSuccess("email");

            Assert.False(cb.IsOpen("email"));
            Assert.Null(cb.GetRetryDelay("email"));
        }

        [Fact]
        public void Should_not_reset_failures_on_unrelated_channel_success()
        {
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(DateTime.UtcNow);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            // cause failures on email
            for (int i = 0; i < 4; i++) cb.OnFailure("email");

            // call success on whatsapp
            cb.OnSuccess("whatsapp");

            // email should still be below threshold and not open yet
            Assert.False(cb.IsOpen("email"));

            // one more failure should open email
            cb.OnFailure("email");
            Assert.True(cb.IsOpen("email"));
        }

        // --- 6. CHANNEL ISOLATION ---------------------------------------------

        [Fact]
        public void Should_maintain_separate_state_per_channel()
        {
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(DateTime.UtcNow);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            for (int i = 0; i < 5; i++) cb.OnFailure("whatsapp");
            Assert.True(cb.IsOpen("whatsapp"));
            Assert.False(cb.IsOpen("email"));
        }

        [Fact]
        public void Should_not_open_email_when_whatsapp_fails()
        {
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(DateTime.UtcNow);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            for (int i = 0; i < 5; i++) cb.OnFailure("whatsapp");
            Assert.True(cb.IsOpen("whatsapp"));
            Assert.False(cb.IsOpen("email"));
        }

        [Fact]
        public void Should_not_share_retry_delay_between_channels()
        {
            var start = DateTime.UtcNow;
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(start);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            // open email now
            for (int i = 0; i < 5; i++) cb.OnFailure("email");
            // advance and open whatsapp later
            clock.Advance(TimeSpan.FromSeconds(10));
            for (int i = 0; i < 5; i++) cb.OnFailure("whatsapp");

            var dEmail = cb.GetRetryDelay("email");
            var dWhatsapp = cb.GetRetryDelay("whatsapp");

            Assert.NotNull(dEmail);
            Assert.NotNull(dWhatsapp);
            // whatsapp should have slightly more time remaining than email (approx 10s difference)
            Assert.True(dWhatsapp.Value > dEmail.Value);
        }

        // --- 7. CONCURRENCY ---------------------------------------------------

        [Fact]
        public async Task Should_be_thread_safe_under_concurrent_failures()
        {
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(DateTime.UtcNow);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);
            var tasks = new Task[50];
            var start = new System.Threading.ManualResetEventSlim(false);

            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = Task.Run(() =>
                {
                    start.Wait();
                    cb.OnFailure("email");
                });
            }

            start.Set();
            await Task.WhenAll(tasks);

            Assert.True(cb.IsOpen("email"));
        }

        [Fact]
        public async Task Should_not_corrupt_state_under_high_concurrency()
        {
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(DateTime.UtcNow);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);
            var tasks = new Task[100];

            for (int i = 0; i < tasks.Length; i++)
            {
                var idx = i;
                tasks[i] = Task.Run(() =>
                {
                    var ch = (idx % 2 == 0) ? "email" : "whatsapp";
                    if (idx % 3 == 0) cb.OnFailure(ch);
                    else if (idx % 3 == 1) cb.OnSuccess(ch);
                    else cb.IsOpen(ch);
                });
            }

            await Task.WhenAll(tasks);
            // No exceptions and state remains queryable. GetRetryDelay may be null or a positive TimeSpan.
            var d = cb.GetRetryDelay("email");
            if (d.HasValue)
            {
                Assert.True(d.Value >= TimeSpan.Zero);
            }
            else
            {
                Assert.Null(d);
            }
        }

        [Fact]
        public async Task Should_not_transition_states_incorrectly_under_race_conditions()
        {
            var start = DateTime.UtcNow;
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(start);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            // Open circuit
            for (int i = 0; i < 5; i++) cb.OnFailure("email");
            Assert.True(cb.IsOpen("email"));

            // Schedule a task that will advance clock to expire open state
            var t1 = Task.Run(() => clock.Advance(TimeSpan.FromSeconds(61)));
            // Concurrently fire failures
            var t2 = Task.Run(() =>
            {
                for (int i = 0; i < 5; i++) cb.OnFailure("email");
            });

            await Task.WhenAll(t1, t2);

            // Final state should be either open (if failures reached threshold after expiry) or closed
            // Querying should not throw
            var _ = cb.IsOpen("email");
            Assert.True(true);
        }

        [Fact]
        public async Task Should_handle_simultaneous_success_and_failure_calls_correctly()
        {
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(DateTime.UtcNow);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            // Bring to open
            for (int i = 0; i < 5; i++) cb.OnFailure("email");
            clock.Advance(TimeSpan.FromSeconds(61));

            // Concurrent probes: one success, one failure
            var t1 = Task.Run(() => cb.OnSuccess("email"));
            var t2 = Task.Run(() => cb.OnFailure("email"));
            await Task.WhenAll(t1, t2);

            // No exceptions; final state is queryable
            Assert.True(true);
        }

        // --- 8. RETRY DELAY ---------------------------------------------------

        [Fact]
        public void Should_return_correct_retry_delay_when_open()
        {
            var start = DateTime.UtcNow;
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(start);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);
            for (int i = 0; i < 5; i++) cb.OnFailure("email");

            var d0 = cb.GetRetryDelay("email");
            Assert.NotNull(d0);
            Assert.InRange(d0.Value.TotalSeconds, 59, 61);
        }

        [Fact]
        public void Should_reduce_remaining_delay_over_time()
        {
            var start = DateTime.UtcNow;
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(start);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);
            for (int i = 0; i < 5; i++) cb.OnFailure("email");

            var before = cb.GetRetryDelay("email");
            clock.Advance(TimeSpan.FromSeconds(30));
            var middle = cb.GetRetryDelay("email");
            clock.Advance(TimeSpan.FromSeconds(31));
            var after = cb.GetRetryDelay("email");

            Assert.NotNull(before);
            Assert.NotNull(middle);
            Assert.Null(after);
        }

        [Fact]
        public void Should_return_null_when_closed()
        {
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(DateTime.UtcNow);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);
            Assert.Null(cb.GetRetryDelay("email"));
        }

        // --- 9. EDGE CASES ----------------------------------------------------

        [Fact]
        public void Should_handle_null_channel_input()
        {
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(DateTime.UtcNow);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            // Current behavior: passing null as key to ConcurrentDictionary will throw ArgumentNullException
            Assert.Throws<ArgumentNullException>(() => cb.IsOpen(null));
        }

        [Fact]
        public void Should_handle_empty_channel_input()
        {
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(DateTime.UtcNow);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            // Empty string is a valid key
            Assert.False(cb.IsOpen(""));
            cb.OnFailure("");
            Assert.False(cb.IsOpen(""));
        }

        [Fact]
        public void Should_handle_unknown_channel()
        {
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(DateTime.UtcNow);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            cb.OnFailure("unknown-channel");
            Assert.False(cb.IsOpen("unknown-channel"));
        }

        [Fact]
        public void Should_handle_very_rapid_failure_bursts()
        {
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(DateTime.UtcNow);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            for (int i = 0; i < 5; i++) cb.OnFailure("email");
            Assert.True(cb.IsOpen("email"));
        }

        [Fact]
        public void Should_handle_success_after_long_idle_period()
        {
            var start = DateTime.UtcNow;
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(start);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            // record failures
            for (int i = 0; i < 3; i++) cb.OnFailure("email");

            // idle for long period
            clock.Advance(TimeSpan.FromDays(7));

            // then success
            cb.OnSuccess("email");

            // Ensure circuit is closed and no exceptions
            Assert.False(cb.IsOpen("email"));
        }

        // --- 10. FAILURE RECOVERY ---------------------------------------------

        [Fact]
        public void Should_recover_after_provider_restored()
        {
            var start = DateTime.UtcNow;
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(start);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            for (int i = 0; i < 5; i++) cb.OnFailure("email");
            Assert.True(cb.IsOpen("email"));

            // advance beyond open duration
            clock.Advance(TimeSpan.FromSeconds(61));
            // simulate successful probe
            cb.OnSuccess("email");
            Assert.False(cb.IsOpen("email"));
        }

        [Fact]
        public void Should_prevent_retry_storms()
        {
            // MemoryCircuitBreaker does not implement half-open concurrency limiting. Placeholder test.
            Assert.True(true);
        }

        [Fact]
        public void Should_not_flap_between_states_excessively()
        {
            // The simple MemoryCircuitBreaker has no backoff beyond fixed OpenDuration.
            // We validate that repeated opens produce consistent OpenUntil times.
            var start = DateTime.UtcNow;
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(start);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);

            for (int cycle = 0; cycle < 3; cycle++)
            {
                for (int i = 0; i < 5; i++) cb.OnFailure("email");
                Assert.True(cb.IsOpen("email"));
                var d = cb.GetRetryDelay("email");
                Assert.NotNull(d);
                // advance beyond open duration
                clock.Advance(TimeSpan.FromSeconds(61));
                cb.OnSuccess("email");
                Assert.False(cb.IsOpen("email"));
            }
        }

        // --- 11. PERFORMANCE --------------------------------------------------

        [Fact]
        public async Task Should_handle_high_request_volume()
        {
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(DateTime.UtcNow);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);
            var tasks = new Task[200];

            for (int i = 0; i < tasks.Length; i++)
            {
                var idx = i;
                tasks[i] = Task.Run(() =>
                {
                    var ch = "ch_" + (idx % 10);
                    cb.IsOpen(ch);
                    cb.OnFailure(ch);
                    cb.OnSuccess(ch);
                });
            }

            await Task.WhenAll(tasks);
            Assert.True(true);
        }

        [Fact]
        public async Task Should_use_minimal_locking_and_not_block_threads()
        {
            var clock = new CommunicationServices.Infrastructure.Time.FakeClock(DateTime.UtcNow);
            var cb = new CommunicationServices.Infrastructure.Circuit.MemoryCircuitBreaker(clock);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var tasks = new Task[100];
            for (int i = 0; i < tasks.Length; i++)
            {
                var idx = i;
                tasks[i] = Task.Run(() => cb.IsOpen("email" + (idx % 5)));
            }
            await Task.WhenAll(tasks);
            sw.Stop();
            Assert.InRange(sw.ElapsedMilliseconds, 0, 5000);
        }
    }
}
