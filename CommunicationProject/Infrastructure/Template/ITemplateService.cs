using System.Threading.Tasks;

namespace CommunicationServices.Infrastructure.Templates
{
    public interface ITemplateService
    {
        Task<string> RenderAsync(string templateCode, string dataJson);
    }
}
