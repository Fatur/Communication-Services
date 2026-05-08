using CommunicationServices.Infrastructure.Enum;
using System.Threading.Tasks;

namespace CommunicationServices.Application.Interfaces
{
    public interface IWhatsAppProvider
    {
        Task SendAsync(Requestor requestor, string tenantId, IList<string> to, string body);
    }
}