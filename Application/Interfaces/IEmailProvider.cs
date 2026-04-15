using System.Threading.Tasks;

namespace CommunicationService.Application.Interfaces
{
    public interface IEmailProvider
    {
        Task SendAsync(string to, string body);
    }
}