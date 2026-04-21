using System.Threading.Tasks;

namespace CommunicationServices.Application.Interfaces
{
    public interface IWhatsAppProvider
    {
        Task SendAsync(string to, string body);
    }
}