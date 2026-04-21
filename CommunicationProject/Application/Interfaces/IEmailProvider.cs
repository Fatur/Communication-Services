using System.Threading.Tasks;

namespace CommunicationServices.Application.Interfaces
{
    public interface IEmailProvider
    {
        Task SendAsync(string to, string body);
    }
}