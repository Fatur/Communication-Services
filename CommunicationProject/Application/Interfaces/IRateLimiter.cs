using System.Threading.Tasks;

namespace CommunicationServices.Application.Interfaces
{
    public interface IRateLimiter
    {
        /// <summary>
        /// Returns true if allowed
        /// </summary>
        Task<bool> TryAcquireAsync(string tenantId, string channel);
    }
}