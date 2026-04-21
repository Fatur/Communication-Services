using System;
using System.Threading.Tasks;

namespace CommunicationServices.Application.Interfaces
{
    public interface ICircuitBreaker
    {
        bool IsOpen(string channel);
        void OnSuccess(string channel);
        void OnFailure(string channel);
        /// <summary>
        /// If circuit is open, returns suggested retry delay. Otherwise returns null.
        /// </summary>
        TimeSpan? GetRetryDelay(string channel);
    }
}