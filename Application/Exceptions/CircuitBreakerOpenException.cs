using System;

namespace CommunicationService.Application.Exceptions
{
    public class CircuitBreakerOpenException : Exception
    {
        public CircuitBreakerOpenException(string message) : base(message)
        {
        }
    }
}
