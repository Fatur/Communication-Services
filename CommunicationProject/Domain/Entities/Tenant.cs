using System;

namespace CommunicationServices.Domain.Entities
{
    public class Tenant
    {
        public required string TenantId { get; set; }
        public string Customer { get; set; } = string.Empty;
        public required string Server { get; set; }
        public string Password { get; set; } = string.Empty;
        public string Dbase { get; set; } = string.Empty;
    }
}