using CommunicationServices.Application.DTOs;
using CommunicationServices.Application.Interfaces;
using CommunicationServices.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace CommunicationServices.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MessagesController : ControllerBase
    {
        private readonly IMessageRepository _repo;

        public MessagesController(IMessageRepository repo)
        {
            _repo = repo;
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] MessageRequest req)
        {
            var requestor = Request.Headers["X-Requestor"].ToString();
            var WebMenuId = Request.Headers["X-WebMenuId"].ToString();
            if (string.IsNullOrEmpty(requestor))
            {
                return BadRequest("Missing X-Requestor header");
            }

            var id = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var message = new MessageLog
            {
                Id = id,
                TenantId = req.TenantId,
                Channel = req.Channel,
                Recipients = req.To,
                Recipient = string.Join(",", req.To),
                TemplateCode = req.TemplateCode,
                Requestor = requestor,
                WebMenuId = int.TryParse(WebMenuId, out var menuId) ? menuId : (int?)null,
                DataJson = req.Data?.ToJsonString() ?? "{}",
                Status = "pending",
                RetryCount = 0,
                ErrorMessage = null,
                NextRetryAt = null,
                ProcessingAt = null,
                CreatedAt = now,
                SentAt = null
            };

            if (req.EmailPayload != null)
            {
                message.EmailJson = JsonSerializer.Serialize(req.EmailPayload);
            }

            if (req.Attachments != null && req.Attachments.Count > 0)
            {
                for (int i = 0; i < req.Attachments.Count; i++)
                {
                    var attach = req.Attachments[i];
                    var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, string.Format("{0}.{1}", attach.FileName, attach.MediaType));
                    System.IO.File.WriteAllBytes(path, Convert.FromBase64String(attach.Base64));
                    message.AttachmentPaths += path + ";";
                }
            }

            await _repo.InsertAsync(message);
            return Ok(new { message_id = id });
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetMessageStatus(Guid id)
        {
            var m = await _repo.GetByIdAsync(id);
            if (m == null) return NotFound();
            var dto = new MessageStatusDto
            {
                Id = m.Id,
                Status = m.Status,
                RetryCount = m.RetryCount,
                SentAt = m.SentAt
            };
            return Ok(dto);
        }
    }
}
