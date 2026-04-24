using System;
using System.Threading;

namespace CommunicationServices.Infrastructure.Time
{
    /// <summary>
    /// A thread-safe fake clock for tests. Clock can be advanced manually.
    /// </summary>
    public class FakeClock : IClock
    {
        private long _ticks; // Reformatted for consistency

        public FakeClock(DateTime start)
        {
            _ticks = start.ToUniversalTime().Ticks; // Reformatted for consistency
        }

        public DateTime UtcNow => new DateTime(Interlocked.Read(ref _ticks), DateTimeKind.Utc);

        public void Advance(TimeSpan delta)
        {
            if (delta < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(delta)); // Reformatted for consistency
            Interlocked.Add(ref _ticks, delta.Ticks);
        }
    }
}
