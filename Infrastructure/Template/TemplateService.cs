using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Scriban;
using Microsoft.Extensions.Logging;
using CommunicationService.Infrastructure.Templates;

namespace CommunicationService.Infrastructure.Templates
{
    public class TemplateService : ITemplateService
    {
        private readonly ILogger<TemplateService> _logger;
        private readonly Dictionary<string, string> _templates = new()
        {
            { "welcome_email", "Hello {{name}}, welcome to our service! Your code: {{code}}" },
            { "otp_whatsapp", "Your OTP is {{otp}}" }
        };

        public TemplateService(ILogger<TemplateService> logger)
        {
            _logger = logger;
        }

        public Task<string> RenderAsync(string templateCode, string dataJson)
        {
            if (!_templates.TryGetValue(templateCode, out var template))
            {
                _logger.LogWarning("Template {TemplateCode} not found, using raw template code", templateCode);
                template = templateCode;
            }

            object model = new Dictionary<string, object>();
            try
            {
                if (!string.IsNullOrWhiteSpace(dataJson))
                {
                    model = JsonSerializer.Deserialize<Dictionary<string, object>>(dataJson) ?? new Dictionary<string, object>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to deserialize template data");
            }

            var parsed = Scriban.Template.Parse(template);
            var result = parsed.Render(model, memberRenamer: member => member.Name);
            return Task.FromResult(result);
        }
    }
}
