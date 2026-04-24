using System;

namespace CommunicationServices.Infrastructure.Time
{
    /// <summary>
    /// Real system clock implementation that returns DateTime.UtcNow.
    /// </summary>
    public class RealClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
