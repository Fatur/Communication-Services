using System;

namespace CommunicationService.Application.Exceptions
{
    public class RateLimitExceededException : Exception
    {
        public RateLimitExceededException(string message) : base(message)
        {
        }
    }
}
