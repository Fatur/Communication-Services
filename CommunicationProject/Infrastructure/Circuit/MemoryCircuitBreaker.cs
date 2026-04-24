using System;
using System.Collections.Concurrent;
using CommunicationServices.Application.Interfaces;

namespace CommunicationServices.Infrastructure.Circuit
{
    public class MemoryCircuitBreaker : ICircuitBreaker
    {
        private class State
        {
            public int Failures;
            public DateTime? OpenUntil;
            public readonly object Lock = new object();
        }

        private readonly ConcurrentDictionary<string, State> _states = new();
        private const int FailureThreshold = 5;
        private static readonly TimeSpan OpenDuration = TimeSpan.FromSeconds(60);
        private readonly CommunicationServices.Infrastructure.Time.IClock _clock;

        public MemoryCircuitBreaker() : this(new CommunicationServices.Infrastructure.Time.RealClock())
        {
        }

        public MemoryCircuitBreaker(CommunicationServices.Infrastructure.Time.IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public bool IsOpen(string channel)
        {
            var state = _states.GetOrAdd(channel, _ => new State());
            lock (state.Lock)
            {
                if (state.OpenUntil.HasValue && state.OpenUntil.Value > _clock.UtcNow)
                    return true;

                return false;
            }
        }

        public void OnSuccess(string channel)
        {
            var state = _states.GetOrAdd(channel, _ => new State());
            lock (state.Lock)
            {
                state.Failures = 0;
                state.OpenUntil = null;
            }
        }

        public void OnFailure(string channel)
        {
            var state = _states.GetOrAdd(channel, _ => new State());
            lock (state.Lock)
            {
                state.Failures++;
                if (state.Failures >= FailureThreshold)
                {
                    state.OpenUntil = _clock.UtcNow.Add(OpenDuration);
                    state.Failures = 0;
                }
            }
        }

        public TimeSpan? GetRetryDelay(string channel)
        {
            var state = _states.GetOrAdd(channel, _ => new State());
            lock (state.Lock)
            {
                if (state.OpenUntil.HasValue && state.OpenUntil.Value > _clock.UtcNow)
                {
                    return state.OpenUntil.Value - _clock.UtcNow;
                }

                return null;
            }
        }
    }
}
