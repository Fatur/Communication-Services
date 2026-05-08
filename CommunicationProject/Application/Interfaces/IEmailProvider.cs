using CommunicationServices.Domain.Entities;
using CommunicationServices.Infrastructure.Enum;
using System.Threading.Tasks;

namespace CommunicationServices.Application.Interfaces
{
    public interface IEmailProvider
    {
        Task SendAsync(Requestor requestor, MessageLog message, string body);
    }
}