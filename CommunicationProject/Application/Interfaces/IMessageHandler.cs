using System.Threading.Tasks;
using CommunicationServices.Domain.Entities;

namespace CommunicationServices.Application.Interfaces
{
    public interface IMessageHandler
    {
        Task HandleAsync(MessageLog message);
    }
}