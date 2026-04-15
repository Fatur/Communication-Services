using System.Threading.Tasks;
using CommunicationService.Domain.Entities;

namespace CommunicationService.Application.Interfaces
{
    public interface IMessageHandler
    {
        Task HandleAsync(MessageLog message);
    }
}