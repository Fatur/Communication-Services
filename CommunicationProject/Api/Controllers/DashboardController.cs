using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using CommunicationServices.Domain.Entities;

namespace CommunicationServices.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardController : ControllerBase
    {
        private readonly IDbConnection _db;
        private readonly ILogger<DashboardController> _logger;
        private const int CommandTimeoutSeconds = 30;

        public DashboardController(IDbConnection db, ILogger<DashboardController> logger)
        {
            _db = db;
            _logger = logger;
        }

        private record SummaryDto(int Total, int Sent, int Pending, int Processing, int Failed);

        private record RecentDto(Guid Id, string TenantId, string Channel, string Status, string Recipient, int RetryCount, DateTime CreatedAt);

        private record ErrorDto(Guid Id, string ErrorMessage, int RetryCount);

        [HttpGet("summary")]
        public async Task<IActionResult> Summary(CancellationToken ct)
        {
            const string sql = @"
SELECT
    COUNT(1) AS Total,
    SUM(CASE WHEN status = 'sent' THEN 1 ELSE 0 END) AS Sent,
    SUM(CASE WHEN status = 'pending' THEN 1 ELSE 0 END) AS Pending,
    SUM(CASE WHEN status = 'processing' THEN 1 ELSE 0 END) AS Processing,
    SUM(CASE WHEN status = 'failed' THEN 1 ELSE 0 END) AS Failed
FROM message_log;";

            var cmd = new CommandDefinition(sql, commandTimeout: CommandTimeoutSeconds, cancellationToken: ct);
            var summary = await _db.QuerySingleAsync<SummaryDto>(cmd);
            return Ok(summary);
        }

        [HttpGet("recent")]
        public async Task<IActionResult> Recent(CancellationToken ct)
        {
            const string sql = @"
SELECT TOP (20)
    id AS Id,
    tenant_id AS TenantId,
    channel AS Channel,
    status AS Status,
    recipient AS Recipient,
    retry_count AS RetryCount,
    created_at AS CreatedAt
FROM message_log
ORDER BY created_at DESC;";

            var cmd = new CommandDefinition(sql, commandTimeout: CommandTimeoutSeconds, cancellationToken: ct);
            var list = await _db.QueryAsync<RecentDto>(cmd);
            return Ok(list);
        }

        [HttpGet("throughput")]
        public async Task<IActionResult> Throughput(CancellationToken ct)
        {
            const string sql = @"
SELECT COUNT(1) AS SentLastMinute
FROM message_log
WHERE sent_at >= DATEADD(minute, -1, GETDATE());";

            var cmd = new CommandDefinition(sql, commandTimeout: CommandTimeoutSeconds, cancellationToken: ct);
            var value = await _db.QuerySingleAsync<int>(cmd);
            return Ok(new { messages_per_minute = value });
        }

        [HttpGet("errors")]
        public async Task<IActionResult> Errors(CancellationToken ct)
        {
            const string sql = @"
SELECT TOP (20)
    id AS Id,
    error_message AS ErrorMessage,
    retry_count AS RetryCount
FROM message_log
WHERE status = 'failed'
ORDER BY created_at DESC;";

            var cmd = new CommandDefinition(sql, commandTimeout: CommandTimeoutSeconds, cancellationToken: ct);
            var list = await _db.QueryAsync<ErrorDto>(cmd);
            return Ok(list);
        }
    }
}
