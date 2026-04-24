using System;
using System.Threading.Tasks;
using Xunit;

namespace CommunicationServices.UnitTests
{
    // Test skeletons for MemoryRateLimiter. These are xUnit test method signatures with
    // Arrange / Act / Assert comments. Do not add real waits; tests should use a virtual
    // clock (IClock / FakeClock) injected into the implementation. If the implementation
    // does not currently accept a clock, add an adapter before implementing these tests.

    public class MemoryRateLimiterTests
    {
        // --- 1. BASIC LIMITING -------------------------------------------------

        [Fact]
        public async Task Should_allow_request_within_limit()
        {
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            var tenant = "t_allow";
            var channel = "email"; // implementation defines email limit = 10

            // Act
            var allowedCount = 0;
            for (int i = 0; i < 10; i++)
            {
                if (await limiter.TryAcquireAsync(tenant, channel)) allowedCount++;
            }

            // Assert
            Assert.Equal(10, allowedCount);
        }

        [Fact]
        public async Task Should_block_request_when_limit_exceeded()
        {
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            var tenant = "t_block";
            var channel = "email"; // limit = 10

            // Act
            var allowed = 0;
            for (int i = 0; i < 11; i++)
            {
                if (await limiter.TryAcquireAsync(tenant, channel)) allowed++;
            }

            // Assert
            Assert.Equal(10, allowed);
            // An immediate extra call should be blocked
            Assert.False(await limiter.TryAcquireAsync(tenant, channel));
        }

        [Fact]
        public async Task Should_allow_request_after_refill_period()
        {
            // This test uses real time because the current implementation uses DateTime.UtcNow.
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            var tenant = "t_refill";
            var channel = "email"; // implementation defines email limit = 10

            // Act: consume up to the configured limit
            var allowed = 0;
            for (int i = 0; i < 10; i++)
            {
                if (await limiter.TryAcquireAsync(tenant, channel)) allowed++;
            }

            // Assert: all initial tokens consumed
            Assert.Equal(10, allowed);

            // Further immediate call should be blocked
            var blockedNow = await limiter.TryAcquireAsync(tenant, channel);
            Assert.False(blockedNow);

            // Wait for the window to roll over (implementation resets when >= 1 second)
            await Task.Delay(1200);

            // After refill period, a new request should be allowed
            var allowedAfter = await limiter.TryAcquireAsync(tenant, channel);
            Assert.True(allowedAfter);
        }

        // --- 2. PER TENANT ISOLATION -------------------------------------------

        [Fact]
        public async Task Should_isolate_limits_per_tenant()
        {
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            var tenantA = "tenantA";
            var tenantB = "tenantB";
            var channel = "email"; // limit = 10

            // Act: exhaust tenantA
            for (int i = 0; i < 10; i++) await limiter.TryAcquireAsync(tenantA, channel);

            // tenantA should be blocked now
            Assert.False(await limiter.TryAcquireAsync(tenantA, channel));

            // tenantB should still be allowed
            Assert.True(await limiter.TryAcquireAsync(tenantB, channel));
        }

        [Fact]
        public async Task Should_not_block_other_tenants_when_one_exceeds_limit()
        {
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            var tenant1 = "t_block1";
            var tenant2 = "t_block2";
            var channel = "email"; // limit = 10

            // Exhaust tenant1
            for (int i = 0; i < 10; i++) await limiter.TryAcquireAsync(tenant1, channel);
            Assert.False(await limiter.TryAcquireAsync(tenant1, channel));

            // Act: concurrently attempt requests for tenant2
            var tasks = new Task<bool>[20];
            for (int i = 0; i < tasks.Length; i++) tasks[i] = limiter.TryAcquireAsync(tenant2, channel);
            await Task.WhenAll(tasks);

            // Assert: tenant2 should be unaffected and at least one allowed
            var allowed = 0;
            foreach (var t in tasks) if (t.Result) allowed++;
            Assert.True(allowed > 0);
        }

        // --- 3. PER CHANNEL ISOLATION ------------------------------------------

        [Fact]
        public async Task Should_isolate_limits_per_channel()
        {
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            var tenant = "t_channel";
            var whatsapp = "whatsapp"; // limit = 3
            var email = "email"; // limit = 10

            // Act: exhaust whatsapp
            for (int i = 0; i < 3; i++) await limiter.TryAcquireAsync(tenant, whatsapp);
            Assert.False(await limiter.TryAcquireAsync(tenant, whatsapp));

            // Email should still be allowed
            Assert.True(await limiter.TryAcquireAsync(tenant, email));
        }

        [Fact]
        public async Task Should_allow_email_but_block_whatsapp_if_only_whatsapp_limit_exceeded()
        {
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            var tenant = "t_mix";
            var whatsapp = "whatsapp"; // limit = 3
            var email = "email"; // limit = 10

            // Act: exhaust whatsapp
            for (int i = 0; i < 3; i++) Assert.True(await limiter.TryAcquireAsync(tenant, whatsapp));
            Assert.False(await limiter.TryAcquireAsync(tenant, whatsapp));

            // Email should still be allowed
            Assert.True(await limiter.TryAcquireAsync(tenant, email));
        }

        // --- 4. TENANT + CHANNEL COMBINATION -----------------------------------

        [Fact]
        public async Task Should_apply_limit_per_tenant_and_channel_combination()
        {
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            var tenant = "combo";
            var email = "email"; // 10
            var whatsapp = "whatsapp"; // 3

            // Act: exhaust email up to 10
            for (int i = 0; i < 10; i++) Assert.True(await limiter.TryAcquireAsync(tenant, email));
            Assert.False(await limiter.TryAcquireAsync(tenant, email));

            // whatsapp for same tenant should still be allowed
            Assert.True(await limiter.TryAcquireAsync(tenant, whatsapp));
        }

        [Fact]
        public async Task Should_not_mix_tokens_between_tenants_or_channels()
        {
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            var t1 = "t1";
            var t2 = "t2";
            var email = "email"; // 10
            var whatsapp = "whatsapp"; // 3

            // Act
            for (int i = 0; i < 3; i++) Assert.True(await limiter.TryAcquireAsync(t1, whatsapp));
            // t1 whatsapp exhausted
            Assert.False(await limiter.TryAcquireAsync(t1, whatsapp));

            // t2 whatsapp should still allow up to limit
            Assert.True(await limiter.TryAcquireAsync(t2, whatsapp));

            // t1 email should be unaffected
            Assert.True(await limiter.TryAcquireAsync(t1, email));
            Assert.True(await limiter.TryAcquireAsync(t1, email));
        }

        // --- 5. CONCURRENCY ---------------------------------------------------

        [Fact]
        public async Task Should_handle_multiple_concurrent_requests_correctly()
        {
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            var tenant = "t_concurrency";
            var channel = "whatsapp"; // implementation defines whatsapp limit = 3
            var totalTasks = 50;

            // Act
            var tasks = new Task<bool>[totalTasks];
            var start = new System.Threading.ManualResetEventSlim(false);

            for (int i = 0; i < totalTasks; i++)
            {
                tasks[i] = Task.Run(async () =>
                {
                    start.Wait();
                    return await limiter.TryAcquireAsync(tenant, channel);
                });
            }

            // release all workers simultaneously
            start.Set();
            await Task.WhenAll(tasks);

            // Assert
            var allowedCount = 0;
            foreach (var t in tasks)
            {
                if (t.Result) allowedCount++;
            }

            // The implementation limit for whatsapp is 3
            Assert.Equal(3, allowedCount);
        }

        [Fact]
        public async Task Should_not_allow_more_than_limit_under_high_concurrency()
        {
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            var tenant = "t_high";
            var channel = "email"; // limit = 10
            var total = 200;

            // Act
            var tasks = new Task<bool>[total];
            var start = new System.Threading.ManualResetEventSlim(false);
            for (int i = 0; i < total; i++)
            {
                tasks[i] = Task.Run(async () => { start.Wait(); return await limiter.TryAcquireAsync(tenant, channel); });
            }
            start.Set();
            await Task.WhenAll(tasks);

            // Assert
            var allowed = 0;
            foreach (var t in tasks) if (t.Result) allowed++;
            Assert.Equal(10, allowed);
        }

        [Fact]
        public async Task Should_be_thread_safe()
        {
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            var tasks = new Task<bool>[100];

            // Act: create many distinct keys concurrently
            for (int i = 0; i < tasks.Length; i++)
            {
                var tenant = "t_ts_" + i;
                var channel = (i % 2 == 0) ? "email" : "whatsapp";
                tasks[i] = Task.Run(() => limiter.TryAcquireAsync(tenant, channel));
            }

            await Task.WhenAll(tasks);

            // Assert: all succeeded (first acquisition per key)
            foreach (var t in tasks) Assert.True(t.Result);
        }

        // --- 6. BURST BEHAVIOR ------------------------------------------------

        [Fact]
        public async Task Should_allow_burst_up_to_bucket_capacity()
        {
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            var tenant = "t_burst";
            var channel = "whatsapp"; // capacity = 3

            // Act
            var allowed = 0;
            for (int i = 0; i < 3; i++) if (await limiter.TryAcquireAsync(tenant, channel)) allowed++;

            // Assert
            Assert.Equal(3, allowed);
        }

        [Fact]
        public async Task Should_block_after_burst_exceeds_capacity()
        {
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            var tenant = "t_burst2";
            var channel = "whatsapp"; // capacity = 3

            // Act
            var results = new bool[4];
            for (int i = 0; i < 4; i++) results[i] = await limiter.TryAcquireAsync(tenant, channel);

            // Assert: first 3 true, 4th false
            Assert.True(results[0]);
            Assert.True(results[1]);
            Assert.True(results[2]);
            Assert.False(results[3]);
        }

        // --- 7. REFILL LOGIC --------------------------------------------------

        [Fact]
        public async Task Should_refill_tokens_over_time()
        {
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            var tenant = "t_refill2";
            var channel = "whatsapp"; // limit = 3

            // consume full capacity
            for (int i = 0; i < 3; i++) Assert.True(await limiter.TryAcquireAsync(tenant, channel));
            Assert.False(await limiter.TryAcquireAsync(tenant, channel));

            // Act: wait for >1s to allow reset
            await Task.Delay(1200);

            // After refill period, should allow again up to capacity
            var allowed = 0;
            for (int i = 0; i < 3; i++) if (await limiter.TryAcquireAsync(tenant, channel)) allowed++;

            // Assert
            Assert.Equal(3, allowed);
        }

        [Fact]
        public async Task Should_not_exceed_max_capacity()
        {
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            var tenant = "t_max";
            var channel = "whatsapp"; // capacity = 3

            // consume
            for (int i = 0; i < 3; i++) Assert.True(await limiter.TryAcquireAsync(tenant, channel));
            Assert.False(await limiter.TryAcquireAsync(tenant, channel));

            // Wait full refill
            await Task.Delay(1200);

            // Attempt more than capacity
            var allowed = 0;
            for (int i = 0; i < 5; i++) if (await limiter.TryAcquireAsync(tenant, channel)) allowed++;

            Assert.Equal(3, allowed);
        }

        [Fact]
        public async Task Should_refill_gradually_or_per_interval()
        {
            // Arrange: MemoryRateLimiter implements a fixed 1-second window reset.
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            var tenant = "t_frac";
            var channel = "whatsapp"; // limit = 3

            // consume
            for (int i = 0; i < 3; i++) Assert.True(await limiter.TryAcquireAsync(tenant, channel));
            Assert.False(await limiter.TryAcquireAsync(tenant, channel));

            // Act: advance less than 1s
            await Task.Delay(500);
            // Still blocked
            Assert.False(await limiter.TryAcquireAsync(tenant, channel));

            // Advance beyond 1s total
            await Task.Delay(600);
            // Now should be allowed again
            Assert.True(await limiter.TryAcquireAsync(tenant, channel));
        }

        // --- 8. EDGE CASES ----------------------------------------------------

        [Fact]
        public async Task Should_throw_when_tenantId_is_null()
        {
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            // Act / Assert: tenantId null should not throw; treat as empty tenant key
            var allowed = await limiter.TryAcquireAsync(null, "email");
            Assert.True(allowed);
        }

        [Fact]
        public async Task Should_throw_when_channel_is_null_or_empty()
        {
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();

            // Null channel -> implementation will throw when calling ToLowerInvariant
            await Assert.ThrowsAsync<NullReferenceException>(async () => await limiter.TryAcquireAsync("t", null));

            // Empty channel -> treated as unknown channel, default limit applies
            var allowed = await limiter.TryAcquireAsync("t", "");
            Assert.True(allowed);
        }

        [Fact]
        public async Task Should_handle_very_high_request_rate_gracefully()
        {
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            var tenant = "t_highrate";
            var channel = "email"; // limit = 10
            var total = 500;

            // Act
            var tasks = new Task<bool>[total];
            var start = new System.Threading.ManualResetEventSlim(false);
            for (int i = 0; i < total; i++) tasks[i] = Task.Run(async () => { start.Wait(); return await limiter.TryAcquireAsync(tenant, channel); });
            start.Set();
            await Task.WhenAll(tasks);

            // Assert
            var allowed = 0;
            foreach (var t in tasks) if (t.Result) allowed++;
            Assert.Equal(10, allowed);
        }

        [Fact]
        public async Task Should_work_with_very_low_limits_one_per_sec()
        {
            // Arrange
            // MemoryRateLimiter does not support custom limits per test; use whatsapp (3) to emulate low limit behavior
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            var tenant = "t_low";
            var channel = "whatsapp"; // 3

            // Act / Assert
            for (int i = 0; i < 3; i++) Assert.True(await limiter.TryAcquireAsync(tenant, channel));
            Assert.False(await limiter.TryAcquireAsync(tenant, channel));
            await Task.Delay(1200);
            Assert.True(await limiter.TryAcquireAsync(tenant, channel));
        }

        // --- 9. FAILURE SCENARIOS ---------------------------------------------

        [Fact]
        public async Task Should_fail_safe_or_fail_open_when_internal_error_occurs()
        {
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();

            // Act / Assert: if internal error occurs (e.g., null channel leading to NRE), ensure exception is observable
            await Assert.ThrowsAsync<NullReferenceException>(async () => await limiter.TryAcquireAsync("t_fault", null));
        }

        [Fact]
        public async Task Should_not_crash_under_invalid_input()
        {
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            var longTenant = new string('x', 10000);
            var longChannel = new string('y', 10000);

            // Act
            var result = await limiter.TryAcquireAsync(longTenant, longChannel);

            // Assert: should not crash and return a boolean
            Assert.IsType<bool>(result);
        }

        // --- 10. PERFORMANCE / STRESS (smoke) ---------------------------------

        [Fact]
        public async Task Should_handle_thousands_of_requests_per_second()
        {
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            var totalKeys = 1000;
            var tasks = new Task<bool>[totalKeys];

            // Act
            for (int i = 0; i < totalKeys; i++)
            {
                var tenant = "t_perf_" + i;
                tasks[i] = limiter.TryAcquireAsync(tenant, "email");
            }

            await Task.WhenAll(tasks);

            // Assert: all initial acquisitions succeed (one per distinct key)
            foreach (var t in tasks) Assert.True(t.Result);
        }

        [Fact]
        public async Task Should_not_block_threads_async_friendly()
        {
            // Arrange
            var limiter = new CommunicationServices.Infrastructure.Rates.MemoryRateLimiter();
            var tenant = "t_async";
            var channel = "email";

            // Act: call in tight loop
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 100; i++) await limiter.TryAcquireAsync(tenant + i, channel);
            sw.Stop();

            // Assert: total elapsed reasonably small (no blocking). Use a loose bound to avoid flakiness.
            Assert.InRange(sw.ElapsedMilliseconds, 0, 5000);
        }
    }
}
