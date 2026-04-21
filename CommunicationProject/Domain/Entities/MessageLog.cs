using System;

namespace CommunicationServices.Domain.Entities
{
    public class MessageLog
    {
        public Guid Id { get; set; }
        public string TenantId { get; set; } = string.Empty;
        public string Channel { get; set; } = string.Empty;
        public string Recipient { get; set; } = string.Empty;
        public string TemplateCode { get; set; } = string.Empty;
        public string DataJson { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int RetryCount { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime? NextRetryAt { get; set; }
        public DateTime? ProcessingAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? SentAt { get; set; }
    }
}