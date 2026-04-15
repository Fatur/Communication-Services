using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;
using Dapper;
using CommunicationService.Application.Interfaces;
using CommunicationService.Domain.Entities;

namespace CommunicationService.Infrastructure.Data
{
    public class DapperMessageRepository : IMessageRepository
    {
        private readonly IDbConnection _db;

        public DapperMessageRepository(IDbConnection db)
        {
            _db = db;
        }

        public async Task InsertAsync(MessageLog message)
        {
            const string sql = @"INSERT INTO message_log (id, tenant_id, channel, recipient, template_code, data_json, status, retry_count, error_message, next_retry_at, processing_at, created_at, sent_at)
VALUES (@Id, @TenantId, @Channel, @Recipient, @TemplateCode, @DataJson, @Status, @RetryCount, @ErrorMessage, @NextRetryAt, @ProcessingAt, @CreatedAt, @SentAt);";

            await _db.ExecuteAsync(sql, message);
        }

        public async Task<MessageLog?> GetByIdAsync(Guid id)
        {
            const string sql = "SELECT * FROM message_log WHERE id = @Id";
            return await _db.QueryFirstOrDefaultAsync<MessageLog>(sql, new { Id = id });
        }

        public async Task<IEnumerable<MessageLog>> ClaimPendingAsync(int batchSize)
        {
            var sql = @"WITH cte AS (
    SELECT TOP (@BatchSize) *
    FROM message_log
    WHERE status = 'pending' AND (next_retry_at IS NULL OR next_retry_at <= GETDATE())
    ORDER BY created_at
)
UPDATE cte
SET status = 'processing', processing_at = GETDATE()
OUTPUT inserted.*;";

            var result = await _db.QueryAsync<MessageLog>(sql, new { BatchSize = batchSize });
            return result;
        }

        public async Task UpdateAsync(MessageLog message)
        {
            const string sql = @"UPDATE message_log SET
status = @Status,
retry_count = @RetryCount,
error_message = @ErrorMessage,
next_retry_at = @NextRetryAt,
processing_at = @ProcessingAt,
sent_at = @SentAt
WHERE id = @Id";

            await _db.ExecuteAsync(sql, message);
        }
    }
}
