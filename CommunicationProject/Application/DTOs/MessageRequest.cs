using System.Text.Json.Nodes;

namespace CommunicationServices.Application.DTOs
{
    public class MessageRequest
    {
        public string TenantId { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public IList<string> To { get; set; } = new List<string>();
        public EmailPayload EmailPayload { get; set; } = new EmailPayload();
        public List<Attachments> Attachments { get; set; } = new List<Attachments>();
        public string TemplateCode { get; set; } = string.Empty;
        public JsonObject? Data { get; set; }
    }

    public class EmailPayload
    {
        public string Subject { get; set; } = string.Empty;
        public IList<string> CC { get; set; } = new List<string>();
        public bool IsHtml { get; set; } = true;

    }

    public class Attachments
    {
        public required string Base64 { get; set; }
        public required string FileName { get; set; }
        public required string MediaType { get; set; }
    }
}