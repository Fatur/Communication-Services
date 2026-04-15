using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using CommunicationService.Application.DTOs;
using CommunicationService.Application.Interfaces;
using CommunicationService.Domain.Entities;

namespace CommunicationService.Api.Controllers
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
            var id = Guid.NewGuid();
            var now = DateTime.UtcNow;
            var message = new MessageLog
            {
                Id = id,
                TenantId = req.TenantId,
                Channel = req.Channel,
                Recipient = req.To,
                TemplateCode = req.TemplateCode,
                DataJson = req.Data?.ToJsonString() ?? "{}",
                Status = "pending",
                RetryCount = 0,
                ErrorMessage = null,
                NextRetryAt = null,
                ProcessingAt = null,
                CreatedAt = now,
                SentAt = null
            };

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
