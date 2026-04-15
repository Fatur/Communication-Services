using System.Threading.Tasks;

namespace CommunicationService.Application.Interfaces
{
    public interface IWhatsAppProvider
    {
        Task SendAsync(string to, string body);
    }
}