using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using CommunicationService.Application.Interfaces;

namespace CommunicationService.Infrastructure.Rates
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
            var bucket = _buckets.GetOrAdd(key, _ => new Bucket { Count = 0, WindowStart = DateTime.UtcNow });
            lock (bucket.Lock)
            {
                var now = DateTime.UtcNow;
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
