using System.Threading.Tasks;

namespace CommunicationService.Infrastructure.Templates
{
    public interface ITemplateService
    {
        Task<string> RenderAsync(string templateCode, string dataJson);
    }
}
