using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using CommunicationServices.Application.Interfaces;

namespace CommunicationServices.Infrastructure.Rates
{
    public class MemoryRateLimiter : IRateLimiter
    {
        private class Bucket
        {
            public int Count;
            public DateTime WindowStart;
            public readonly object Lock = new object();
        }

        private readonly ConcurrentDictionary<string, Bucket> _buckets = new();
        private readonly CommunicationServices.Infrastructure.Time.IClock _clock;

        public MemoryRateLimiter() : this(new CommunicationServices.Infrastructure.Time.RealClock())
        {
        }

        // For production, callers should inject a real clock implementation that wraps DateTime.UtcNow.
        public MemoryRateLimiter(CommunicationServices.Infrastructure.Time.IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        private int GetLimit(string channel)
        {
            return channel.ToLowerInvariant() switch
            {
                "email" => 10,
                "whatsapp" => 3,
                _ => 5
            };
        }

        public Task<bool> TryAcquireAsync(string tenantId, string channel)
        {
            var key = $"{tenantId}:{channel}";
            var bucket = _buckets.GetOrAdd(key, _ => new Bucket { Count = 0, WindowStart = _clock.UtcNow });
            lock (bucket.Lock)
            {
                var now = _clock.UtcNow;
                if ((now - bucket.WindowStart).TotalSeconds >= 1)
                {
                    bucket.Count = 0;
                    bucket.WindowStart = now;
                }

                var limit = GetLimit(channel);
                if (bucket.Count >= limit)
                {
                    return Task.FromResult(false);
                }

                bucket.Count++;
                return Task.FromResult(true);
            }
        }
    }
}
