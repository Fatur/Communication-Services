using System;

namespace CommunicationService.Application.DTOs
{
    public class MessageStatusDto
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = string.Empty;
        public int RetryCount { get; set; }
        public DateTime? SentAt { get; set; }
    }
}