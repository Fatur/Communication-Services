using System;

namespace CommunicationServices.Application.Exceptions
{
    public class RateLimitExceededException : Exception
    {
        public RateLimitExceededException(string message) : base(message)
        {
        }
    }
}
