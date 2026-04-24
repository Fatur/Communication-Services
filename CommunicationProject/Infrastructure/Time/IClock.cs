using System;

namespace CommunicationServices.Infrastructure.Time
{
    /// <summary>
    /// Abstraction for time in the system to allow deterministic tests.
    /// Implementations should return UTC timestamps.
    /// </summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
    }
}
