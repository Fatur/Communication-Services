using System.Text.Json.Nodes;

namespace CommunicationService.Application.DTOs
{
    public class MessageRequest
    {
        public string TenantId { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string TemplateCode { get; set; } = string.Empty;
        public JsonObject? Data { get; set; }
    }
}