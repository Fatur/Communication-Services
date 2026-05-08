using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using CommunicationServices.Application.Interfaces;
using CommunicationServices.Domain.Entities;

namespace CommunicationServices.Infrastructure.Data
{
    /// <summary>
    /// Dapper-based repository for message_log.
    /// IMPORTANT: IDbConnection must be registered as Scoped, NOT Singleton. A shared/singleton connection
    /// will break concurrency and transaction semantics under load.
    /// </summary>
    public class DapperMessageRepository : IMessageRepository
    {
        private readonly IDbConnection _db;
        private const int CommandTimeoutSeconds = 30;
        private readonly ILogger<DapperMessageRepository>? _logger;

        public DapperMessageRepository(IDbConnection db, ILogger<DapperMessageRepository>? logger = null)
        {
            _db = db;
            _logger = logger;
        }

        /*
         Recommended index to improve performance of ClaimPendingAsync and avoid table scans:
         CREATE NONCLUSTERED INDEX IX_message_log_processing
         ON message_log (status, next_retry_at, processing_at, created_at)
         INCLUDE (id);

         This index allows the query engine to quickly find candidate rows for claiming without scanning the full table.
        */

        public async Task InsertAsync(MessageLog message, CancellationToken ct = default)
        {
            message.Recipient = string.Join(",", message.Recipients);
            const string sql = @"INSERT INTO message_log (id, tenant_id, requestor, channel, web_menu_id, recipient, template_code, email_json, data_json, attachment_path, status, retry_count, error_message, next_retry_at, processing_at, created_at, sent_at)
VALUES (@Id, @TenantId, @Requestor, @Channel, @WebMenuId, @Recipient, @TemplateCode, @EmailJson, @DataJson, @AttachmentPaths, @Status, @RetryCount, @ErrorMessage, @NextRetryAt, @ProcessingAt, @CreatedAt, @SentAt);";

            var cmd = new CommandDefinition(sql, message, commandTimeout: CommandTimeoutSeconds, cancellationToken: ct);
            await _db.ExecuteAsync(cmd);
        }

        public async Task<MessageLog?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            const string sql = "SELECT id, tenant_id AS TenantId, requestor, channel, recipient, template_code AS TemplateCode, email_json AS EmailJson, data_json AS DataJson, attachment_path AS AttachmentPaths, status, retry_count AS RetryCount, error_message AS ErrorMessage, next_retry_at AS NextRetryAt, processing_at AS ProcessingAt, created_at AS CreatedAt, sent_at AS SentAt FROM message_log WHERE id = @Id";
            var cmd = new CommandDefinition(sql, new { Id = id }, commandTimeout: CommandTimeoutSeconds, cancellationToken: ct);
            var result = await _db.QuerySingleOrDefaultAsync<MessageLog>(cmd);
            if (result != null) {
                result.Recipients = result.Recipient?.Split(',') ?? Array.Empty<string>();
            }
            return result;
        }

        /// <summary>
        /// Claim a batch of pending messages for processing.
        /// Uses low-level locking hints to avoid blocking and to skip rows that are already locked by other workers.
        /// Also recovers stuck messages that have been in 'processing' state for longer than 5 minutes.
        /// </summary>
        /// <remarks>
        /// Locking hints:
        ///  - ROWLOCK: take row-level locks
        ///  - READPAST: skip rows locked by other transactions (prevents blocking)
        ///  - UPDLOCK: take update locks to prevent races when selecting rows to update
        ///
        /// Stuck processing recovery: we include rows that were marked 'processing' but haven't been updated for >5 minutes
        /// so they can be retried by other workers.
        /// </remarks>
        public async Task<IEnumerable<MessageLog>> ClaimPendingAsync(int batchSize, CancellationToken ct = default)
        {
            var sql = @"
WITH cte AS (
    SELECT TOP (@BatchSize) id
    FROM message_log WITH (ROWLOCK, READPAST, UPDLOCK)
    WHERE (
        status = 'pending'
        AND (next_retry_at IS NULL OR next_retry_at <= GETDATE())
    )
    OR (
        status = 'processing'
        AND processing_at < DATEADD(minute, -5, GETDATE())
    )
    ORDER BY created_at, id
)
UPDATE m
SET
    status = 'processing',
    processing_at = GETDATE()
OUTPUT
    inserted.id AS Id,
    inserted.tenant_id AS TenantId,
    inserted.requestor AS Requestor,
    inserted.channel AS Channel,
    inserted.web_menu_id AS WebMenuId,
    inserted.recipient AS Recipient,
    inserted.template_code AS TemplateCode,
    inserted.email_json AS EmailJson,
    inserted.data_json AS DataJson,
    inserted.attachment_path AS AttachmentPaths,
    inserted.status AS Status,
    inserted.retry_count AS RetryCount,
    inserted.error_message AS ErrorMessage,
    inserted.next_retry_at AS NextRetryAt,
    inserted.processing_at AS ProcessingAt,
    inserted.created_at AS CreatedAt,
    inserted.sent_at AS SentAt
FROM message_log m
JOIN cte ON m.id = cte.id;";

            var cmd = new CommandDefinition(sql, new { BatchSize = batchSize }, commandTimeout: CommandTimeoutSeconds, cancellationToken: ct);
            var result = await _db.QueryAsync<MessageLog>(cmd);
            return result;
        }

        /// <summary>
        /// Update message state. Only update if the row is currently in 'processing' state to avoid
        /// clobbering changes made by other workers.
        /// </summary>
        public async Task UpdateAsync(MessageLog message, CancellationToken ct = default)
        {
            const string sql = @"UPDATE message_log SET
status = @Status,
retry_count = @RetryCount,
error_message = @ErrorMessage,
next_retry_at = @NextRetryAt,
processing_at = @ProcessingAt,
sent_at = @SentAt
WHERE id = @Id AND status = 'processing'";
            var cmd = new CommandDefinition(sql, message, commandTimeout: CommandTimeoutSeconds, cancellationToken: ct);
            var affected = await _db.ExecuteAsync(cmd);

            // If no rows were affected, another worker may have modified the row (race) or the message is not in 'processing' state.
            // This is expected in high-concurrency scenarios and indicates this worker should not assume success.
            if (affected == 0)
            {
                _logger?.LogWarning("UpdateAsync affected 0 rows for message {MessageId}. Possible concurrent update or message no longer in 'processing' state.", message?.Id);
            }
        }
    }
}
