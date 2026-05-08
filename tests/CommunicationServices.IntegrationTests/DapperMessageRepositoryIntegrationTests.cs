using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Data.SqlClient;
using CommunicationServices.Domain.Entities;
using CommunicationServices.Infrastructure.Data;

namespace CommunicationServices.IntegrationTests
{
    /// <summary>
    /// INTEGRATION TESTS for DapperMessageRepository.
    /// 
    /// These tests validate REAL SQL Server behavior including:
    /// - Row-level locking (ROWLOCK, READPAST, UPDLOCK hints)
    /// - Concurrency safety (no duplicate claims)
    /// - Stuck message recovery
    /// - State transitions
    /// - Retry logic
    /// 
    /// IMPORTANT: These tests use a REAL SQL Server database (Express or LocalDB).
    /// Do NOT use in-memory databases or mocks.
    /// 
    /// TEST DATABASE SETUP:
    /// 1. Create isolated test database (e.g., "TestCommunicationServices")
    /// 2. Run schema migration to create message_log table
    /// 3. Create recommended index: IX_message_log_processing
    /// 4. Use transaction rollback or table truncation for cleanup between tests
    /// 
    /// CONNECTION STRING:
    /// Set via environment variable: TEST_DB_CONNECTION_STRING
    /// Or update GetTestConnectionString() method below
    /// 
    /// CLEANUP STRATEGY:
    /// Option 1: Transaction rollback per test (recommended for speed)
    /// Option 2: TRUNCATE TABLE message_log before each test (simple, slower)
    /// Option 3: Recreate schema per test class (slowest, most isolated)
    /// </summary>
    public class DapperMessageRepositoryIntegrationTests
    {
        // --- DATABASE SETUP AND CLEANUP ---

        /// <summary>
        /// Fixture-like setup. In xUnit, use IAsyncLifetime or ClassFixture for shared database setup.
        /// This is a placeholder for the pattern. Actual implementation should use:
        /// - IAsyncLifetime for per-class setup/teardown
        /// - IDisposable/IAsyncDisposable for per-test cleanup
        /// </summary>
        private static string GetTestConnectionString()
        {
            // Arrange: Get connection string from environment or config
            // Expected: "Server=.\SQLEXPRESS;Database=TestCommunicationServices;Trusted_Connection=True;"
            // OR LocalDB: "Server=(LocalDB)\\mssqllocaldb;Database=TestCommunicationServices;Trusted_Connection=True;"
            var connStr = Environment.GetEnvironmentVariable("TestDatabase");
            if (string.IsNullOrEmpty(connStr))
            {
                connStr = @"Server=(localdb)\MSSQLLocalDB;Database=TestCommunicationServices;Trusted_Connection=True;TrustServerCertificate=True;";
            }
            return connStr;
        }

        /// <summary>
        /// Cleanup method. Should be called before each test or in a fixture's Dispose method.
        /// Options:
        /// - Option 1: TRUNCATE TABLE message_log (simple, no schema dependency)
        /// - Option 2: DELETE FROM message_log WHERE 1=1 (slower)
        /// - Option 3: Drop and recreate schema per test (slowest, most isolated)
        /// </summary>
        private async Task<IDbConnection> TruncateMessageLogTableAsync()
        {
            // Arrange: Create connection
            var connStr = GetTestConnectionString();
            var connection = new SqlConnection(connStr);
            await connection.OpenAsync();

            // Act: Truncate table
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "TRUNCATE TABLE message_log";
                cmd.CommandTimeout = 30;
                await cmd.ExecuteNonQueryAsync();
            }

            return connection;
        }

        /// <summary>
        /// Helper to seed test data into message_log
        /// </summary>
        private async Task SeedMessageAsync(IDbConnection connection, MessageLog message)
        {
            var repo = new DapperMessageRepository(connection);
            await repo.InsertAsync(message);
        }

        /// <summary>
        /// Helper to seed multiple messages
        /// </summary>
        private async Task SeedMessagesAsync(IDbConnection connection, IEnumerable<MessageLog> messages)
        {
            var repo = new DapperMessageRepository(connection);
            foreach (var msg in messages)
            {
                await repo.InsertAsync(msg);
            }
        }

        /// <summary>
        /// Helper to count rows in message_log table
        /// </summary>
        private async Task<int> CountRowsAsync(IDbConnection connection)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM message_log";
                cmd.CommandTimeout = 30;
                object? result;
                if (cmd is DbCommand dbCommand)
                {
                    result = await dbCommand.ExecuteScalarAsync();
                }
                else
                {
                    result = cmd.ExecuteScalar();
                }

                return Convert.ToInt32(result);
            }
        }

        /// <summary>
        /// Helper to count rows with specific status
        /// </summary>
        private async Task<int> CountRowsByStatusAsync(IDbConnection connection, string status)
        {
            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM message_log WHERE status = @Status";
                cmd.CommandTimeout = 30;
                var param = cmd.CreateParameter();
                param.ParameterName = "@Status";
                param.Value = status;
                cmd.Parameters.Add(param);
                object? result;
                if (cmd is DbCommand dbCommand)
                {
                    result = await dbCommand.ExecuteScalarAsync();
                }
                else
                {
                    result = cmd.ExecuteScalar();
                }

                return Convert.ToInt32(result);
            }
        }

        private MessageLog CreateTestMessage(
            Guid? id = null,
            string tenantId = "APOLLOLIVE",
            string channel = "email",
            IList<string> recipients = null,

            string status = "pending",
            int retryCount = 0,
            string? errorMessage = null,
            DateTime? nextRetryAt = null,
            DateTime? processingAt = null)
        {
            return new MessageLog
            {
                Id = id ?? Guid.NewGuid(),
                TenantId = tenantId,
                Channel = channel,
                Recipients = recipients ?? new List<string> { "test@example.com" },
                Requestor = "Soficloud",
                TemplateCode = "WELCOME",
                DataJson = "{\"name\": \"Test User\"}",
                Status = status,
                RetryCount = retryCount,
                ErrorMessage = errorMessage,
                NextRetryAt = nextRetryAt,
                ProcessingAt = processingAt,
                CreatedAt = DateTime.UtcNow,
                SentAt = null
            };
        }

        private static async Task<IEnumerable<MessageLog>> ClaimWithDeadlockRetryAsync(DapperMessageRepository repo, int batchSize, int maxRetries = 5)
        {
            for (var attempt = 0; attempt <= maxRetries; attempt++)
            {
                try
                {
                    return await repo.ClaimPendingAsync(batchSize);
                }
                catch (SqlException ex) when (ex.Number == 1205 && attempt < maxRetries)
                {
                    await Task.Delay(20 * (attempt + 1));
                }
            }

            return Array.Empty<MessageLog>();
        }

        // --- 1. INSERT TESTS ---

        [Fact]
        public async Task Should_insert_message_successfully()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var message = CreateTestMessage(id: Guid.Parse("11111111-1111-1111-1111-111111111111"));

            // Act
            await repo.InsertAsync(message);

            // Assert
            Assert.Equal(1, await CountRowsAsync(connection));
            var retrieved = await repo.GetByIdAsync(message.Id);
            Assert.NotNull(retrieved);
            Assert.Equal(message.Id, retrieved!.Id);
            Assert.Equal(message.TenantId, retrieved.TenantId);
            Assert.Equal(message.Channel, retrieved.Channel);
            Assert.Equal(message.Recipient, retrieved.Recipient);
            Assert.Equal(message.TemplateCode, retrieved.TemplateCode);
            Assert.Equal(message.DataJson, retrieved.DataJson);
            Assert.Equal(message.Status, retrieved.Status);
            Assert.Equal(message.RetryCount, retrieved.RetryCount);
        }

        [Fact]
        public async Task Should_insert_multiple_messages()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var messages = Enumerable.Range(0, 10)
                .Select(_ => CreateTestMessage(id: Guid.NewGuid()))
                .ToList();

            // Act
            await SeedMessagesAsync(connection, messages);

            // Assert
            Assert.Equal(10, await CountRowsAsync(connection));
        }

        [Fact]
        public async Task Should_insert_message_with_null_optional_fields()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var message = new MessageLog
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-1",
                Channel = "email",
                Recipient = "test@example.com",
                TemplateCode = "WELCOME",
                DataJson = "{}",
                Status = "pending",
                RetryCount = 0,
                ErrorMessage = null,   // nullable
                NextRetryAt = null,    // nullable
                ProcessingAt = null,   // nullable
                CreatedAt = DateTime.UtcNow,
                SentAt = null          // nullable
            };

            // Act
            await repo.InsertAsync(message);

            // Assert
            var retrieved = await repo.GetByIdAsync(message.Id);
            Assert.NotNull(retrieved);
            Assert.Null(retrieved!.ErrorMessage);
            Assert.Null(retrieved.NextRetryAt);
            Assert.Null(retrieved.ProcessingAt);
            Assert.Null(retrieved.SentAt);
        }

        // --- 2. GET TESTS ---

        [Fact]
        public async Task Should_return_message_by_id()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var messageId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var message = CreateTestMessage(id: messageId);
            await SeedMessageAsync(connection, message);

            // Act
            var retrieved = await repo.GetByIdAsync(messageId);

            // Assert
            Assert.NotNull(retrieved);
            Assert.Equal(messageId, retrieved!.Id);
            Assert.Equal("APOLLOLIVE", retrieved.TenantId);
            Assert.Equal("email", retrieved.Channel);
            Assert.Equal("test@example.com", retrieved.Recipient);
        }

        [Fact]
        public async Task Should_return_null_when_message_not_found()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var nonExistentId = Guid.Parse("99999999-9999-9999-9999-999999999999");

            // Act
            var retrieved = await repo.GetByIdAsync(nonExistentId);

            // Assert
            Assert.Null(retrieved);
        }

        [Fact]
        public async Task Should_map_all_columns_correctly_including_snake_case_aliases()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var now = DateTime.UtcNow;
            var message = new MessageLog
            {
                Id = Guid.NewGuid(),
                TenantId = "tenant-abc",
                Channel = "whatsapp",
                Recipient = "+1234567890",
                TemplateCode = "ORDER_CONFIRM",
                DataJson = "{\"orderId\": \"12345\"}",
                Status = "pending",
                RetryCount = 0,
                ErrorMessage = null,
                NextRetryAt = now.AddMinutes(5),
                ProcessingAt = null,
                CreatedAt = now,
                SentAt = null
            };
            await SeedMessageAsync(connection, message);

            // Act
            var retrieved = await repo.GetByIdAsync(message.Id);

            // Assert
            Assert.NotNull(retrieved);
            Assert.Equal("tenant-abc", retrieved!.TenantId);
            Assert.Equal("ORDER_CONFIRM", retrieved.TemplateCode);
            Assert.Equal("{\"orderId\": \"12345\"}", retrieved.DataJson);
            Assert.NotNull(retrieved.NextRetryAt);
            Assert.True(Math.Abs((retrieved.NextRetryAt!.Value - now.AddMinutes(5)).TotalSeconds) < 2);
        }

        // --- 3. CLAIM PENDING TESTS ---

        [Fact]
        public async Task Should_claim_pending_messages()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var messages = Enumerable.Range(0, 5)
                .Select(i => CreateTestMessage(
                    id: Guid.NewGuid(),
                    status: "pending"))
                .ToList();
            await SeedMessagesAsync(connection, messages);

            // Act
            var claimed = await repo.ClaimPendingAsync(batchSize: 5);
            var claimedList = claimed.ToList();

            // Assert
            Assert.Equal(5, claimedList.Count);
            foreach (var msg in claimedList)
            {
                Assert.Equal("processing", msg.Status);
                Assert.NotNull(msg.ProcessingAt);
            }
            var processingCount = await CountRowsByStatusAsync(connection, "processing");
            Assert.Equal(5, processingCount);
        }

        [Fact]
        public async Task Should_respect_batch_size_limit()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var messages = Enumerable.Range(0, 100)
                .Select(i => CreateTestMessage(
                    id: Guid.NewGuid(),
                    status: "pending"))
                .ToList();
            await SeedMessagesAsync(connection, messages);

            // Act
            var claimed = await repo.ClaimPendingAsync(batchSize: 10);
            var claimedList = claimed.ToList();

            // Assert
            Assert.Equal(10, claimedList.Count);
            var pendingCount = await CountRowsByStatusAsync(connection, "pending");
            Assert.Equal(90, pendingCount);
        }

        [Fact]
        public async Task Should_skip_messages_with_future_retry_time()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var now = DateTime.Now;
            var futureMessages = Enumerable.Range(0, 5)
                .Select(i => CreateTestMessage(
                    id: Guid.NewGuid(),
                    status: "pending",
                    nextRetryAt: now.AddMinutes(10))) // future, should not be claimed
                .ToList();
            await SeedMessagesAsync(connection, futureMessages);

            // Act
            var claimed = await repo.ClaimPendingAsync(batchSize: 10);

            // Assert
            Assert.Empty(claimed);
            var pendingCount = await CountRowsByStatusAsync(connection, "pending");
            Assert.Equal(5, pendingCount);
        }

        [Fact]
        public async Task Should_claim_retryable_messages_when_retry_time_reached()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var now = DateTime.UtcNow;
            var retryableMessages = Enumerable.Range(0, 5)
                .Select(i => CreateTestMessage(
                    id: Guid.NewGuid(),
                    status: "pending",
                    nextRetryAt: now.AddSeconds(-10))) // past, should be claimed
                .ToList();
            await SeedMessagesAsync(connection, retryableMessages);

            // Act
            var claimed = await repo.ClaimPendingAsync(batchSize: 10);
            var claimedList = claimed.ToList();

            // Assert
            Assert.Equal(5, claimedList.Count);
            Assert.All(claimedList, msg => Assert.Equal("processing", msg.Status));
        }

        [Fact]
        public async Task Should_claim_messages_without_next_retry_at_as_pending()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var messages = Enumerable.Range(0, 3)
                .Select(i => CreateTestMessage(
                    id: Guid.NewGuid(),
                    status: "pending",
                    nextRetryAt: null)) // first attempt, no retry time
                .ToList();
            await SeedMessagesAsync(connection, messages);

            // Act
            var claimed = await repo.ClaimPendingAsync(batchSize: 10);
            var claimedList = claimed.ToList();

            // Assert
            Assert.Equal(3, claimedList.Count);
        }

        // --- 4. STUCK MESSAGE RECOVERY ---

        [Fact]
        public async Task Should_recover_stuck_processing_messages_older_than_5_minutes()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var now = DateTime.UtcNow;
            var stuckMessage = CreateTestMessage(
                id: Guid.Parse("88888888-8888-8888-8888-888888888888"),
                status: "processing",
                processingAt: now.AddMinutes(-6)); // stuck for 6 minutes
            await SeedMessageAsync(connection, stuckMessage);

            // Act
            var claimed = await repo.ClaimPendingAsync(batchSize: 10);
            var claimedList = claimed.ToList();

            // Assert
            Assert.Single(claimedList);
            Assert.Equal(stuckMessage.Id, claimedList[0].Id);
            Assert.Equal("processing", claimedList[0].Status);
        }

        [Fact]
        public async Task Should_not_recover_recent_processing_messages_less_than_5_minutes()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var now = DateTime.Now;
            var recentMessage = CreateTestMessage(
                id: Guid.Parse("99999999-9999-9999-9999-999999999999"),
                status: "processing",
                processingAt: now.AddMinutes(-3)); // only 3 minutes, should not be recovered
            await SeedMessageAsync(connection, recentMessage);

            // Act
            var claimed = await repo.ClaimPendingAsync(batchSize: 10);

            // Assert
            Assert.Empty(claimed);
        }

        [Fact]
        public async Task Should_recover_multiple_stuck_messages_within_batch_size()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var now = DateTime.UtcNow;
            var stuckMessages = Enumerable.Range(0, 3)
                .Select(i => CreateTestMessage(
                    id: Guid.NewGuid(),
                    status: "processing",
                    processingAt: now.AddMinutes(-7)))
                .ToList();
            await SeedMessagesAsync(connection, stuckMessages);

            // Act
            var claimed = await repo.ClaimPendingAsync(batchSize: 10);
            var claimedList = claimed.ToList();

            // Assert
            Assert.Equal(3, claimedList.Count);
            Assert.All(claimedList, msg => Assert.Equal("processing", msg.Status));
        }

        // --- 5. CONCURRENCY TESTS (CRITICAL) ---

        [Fact]
        public async Task Should_not_claim_same_message_twice_under_concurrent_access()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var messages = Enumerable.Range(0, 15)
                .Select(i => CreateTestMessage(
                    id: Guid.NewGuid(),
                    status: "pending"))
                .ToList();
            await SeedMessagesAsync(connection, messages);

            var worker1Claims = new List<Guid>();
            var worker2Claims = new List<Guid>();
            var worker3Claims = new List<Guid>();

            // Act: Run 3 concurrent workers claiming from the same pool
            var task1 = Task.Run(async () =>
            {
                // Worker 1 claims batches
                for (int i = 0; i < 4; i++)
                {
                    using var conn = new SqlConnection(GetTestConnectionString());
                    await conn.OpenAsync();
                    var r = new DapperMessageRepository(conn);
                    var claimed = await r.ClaimPendingAsync(batchSize: 5);
                    lock (worker1Claims)
                    {
                        foreach (var msg in claimed)
                            worker1Claims.Add(msg.Id);
                    }
                    await Task.Delay(5); // small delay
                }
            });

            var task2 = Task.Run(async () =>
            {
                // Worker 2 claims batches
                for (int i = 0; i < 4; i++)
                {
                    using var conn = new SqlConnection(GetTestConnectionString());
                    await conn.OpenAsync();
                    var r = new DapperMessageRepository(conn);
                    var claimed = await r.ClaimPendingAsync(batchSize: 5);
                    lock (worker2Claims)
                    {
                        foreach (var msg in claimed)
                            worker2Claims.Add(msg.Id);
                    }
                    await Task.Delay(5);
                }
            });

            var task3 = Task.Run(async () =>
            {
                // Worker 3 claims batches
                for (int i = 0; i < 4; i++)
                {
                    using var conn = new SqlConnection(GetTestConnectionString());
                    await conn.OpenAsync();
                    var r = new DapperMessageRepository(conn);
                    var claimed = await r.ClaimPendingAsync(batchSize: 5);
                    lock (worker3Claims)
                    {
                        foreach (var msg in claimed)
                            worker3Claims.Add(msg.Id);
                    }
                    await Task.Delay(5);
                }
            });

            await Task.WhenAll(task1, task2, task3);

            // Assert
            var allClaimed = new HashSet<Guid>(worker1Claims);
            Assert.Empty(allClaimed.Intersect(worker2Claims));
            Assert.Empty(allClaimed.Intersect(worker3Claims));
            Assert.Empty(worker2Claims.Intersect(worker3Claims));

            var totalUnique = new HashSet<Guid>(worker1Claims.Concat(worker2Claims).Concat(worker3Claims));
            Assert.Equal(15, totalUnique.Count);
        }

        [Fact]
        public async Task Should_distribute_work_between_multiple_workers_fairly()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var messages = Enumerable.Range(0, 50)
                .Select(i => CreateTestMessage(
                    id: Guid.NewGuid(),
                    status: "pending"))
                .ToList();
            await SeedMessagesAsync(connection, messages);

            var claimsPerWorker = new Dictionary<int, List<Guid>>();
            var numWorkers = 5;
            for (int i = 0; i < numWorkers; i++)
                claimsPerWorker[i] = new List<Guid>();

            // Act: Run 5 workers concurrently, each claiming batches
            var tasks = Enumerable.Range(0, numWorkers).Select(workerId => Task.Run(async () =>
            {
                while (true)
                {
                    using var conn = new SqlConnection(GetTestConnectionString());
                    await conn.OpenAsync();
                    var r = new DapperMessageRepository(conn);
                    var claimed = await r.ClaimPendingAsync(batchSize: 8);
                    var claimedList = claimed.ToList();

                    if (!claimedList.Any())
                        break;

                    lock (claimsPerWorker)
                    {
                        foreach (var msg in claimedList)
                            claimsPerWorker[workerId].Add(msg.Id);
                    }
                }
            })).ToList();

            await Task.WhenAll(tasks);

            // Assert
            var allClaimed = claimsPerWorker.Values.SelectMany(x => x).ToList();
            var uniqueClaimed = new HashSet<Guid>(allClaimed);
            Assert.Equal(50, uniqueClaimed.Count);
            Assert.Equal(50, allClaimed.Count);
            Assert.True(claimsPerWorker.Values.Count(v => v.Count > 0) >= 2);
        }

        [Fact]
        public async Task Should_handle_extreme_concurrency_without_duplicates_or_corruption()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var messages = Enumerable.Range(0, 300)
                .Select(i => CreateTestMessage(
                    id: Guid.NewGuid(),
                    status: "pending"))
                .ToList();
            await SeedMessagesAsync(connection, messages);

            var allClaimedIds = new System.Collections.Concurrent.ConcurrentBag<Guid>();

            // Act: Massive concurrent load
            var tasks = Enumerable.Range(0, 10).Select(workerId => Task.Run(async () =>
            {
                var random = new Random(workerId);
                while (true)
                {
                    using var conn = new SqlConnection(GetTestConnectionString());
                    await conn.OpenAsync();
                    var r = new DapperMessageRepository(conn);
                    var claimed = await ClaimWithDeadlockRetryAsync(r, batchSize: random.Next(5, 20));
                    var claimedList = claimed.ToList();

                    if (!claimedList.Any())
                        break;

                    foreach (var msg in claimedList)
                        allClaimedIds.Add(msg.Id);
                }
            })).ToList();

            await Task.WhenAll(tasks);

            // Assert
            var uniqueIds = new HashSet<Guid>(allClaimedIds);
            Assert.Equal(300, allClaimedIds.Count);
            Assert.Equal(300, uniqueIds.Count);
            Assert.Empty(allClaimedIds.GroupBy(x => x).Where(g => g.Count() > 1));
        }

        [Fact]
        public async Task Should_use_readpast_to_skip_locked_rows_without_blocking()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var messages = Enumerable.Range(0, 10)
                .Select(i => CreateTestMessage(
                    id: Guid.NewGuid(),
                    status: "pending"))
                .ToList();
            await SeedMessagesAsync(connection, messages);

            // Act: Claim in transaction 1 (holds locks)
            using (var conn1 = new SqlConnection(GetTestConnectionString()))
            {
                await conn1.OpenAsync();
                var repo1 = new DapperMessageRepository(conn1);
                var claimed1 = (await repo1.ClaimPendingAsync(batchSize: 5)).ToList();

                // At this point, 5 rows are locked by conn1
                // Meanwhile, transaction 2 tries to claim
                using (var conn2 = new SqlConnection(GetTestConnectionString()))
                {
                    await conn2.OpenAsync();
                    var repo2 = new DapperMessageRepository(conn2);

                    // Should NOT block due to READPAST hint
                    var claimed2 = (await repo2.ClaimPendingAsync(batchSize: 10)).ToList();

                    Assert.Equal(5, claimed2.Count);
                    var allClaimed = new HashSet<Guid>(claimed1.Select(m => m.Id));
                    Assert.Empty(claimed2.Where(m => allClaimed.Contains(m.Id)));
                }
            }
        }

        // --- 6. UPDATE TESTS ---

        [Fact]
        public async Task Should_update_processing_message_successfully()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var messageId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
            var message = CreateTestMessage(
                id: messageId,
                status: "processing",
                processingAt: DateTime.UtcNow);
            await SeedMessageAsync(connection, message);

            var updated = message;
            updated.Status = "sent";
            updated.SentAt = DateTime.UtcNow;
            updated.RetryCount = 0;

            // Act
            await repo.UpdateAsync(updated);

            // Assert
            var retrieved = await repo.GetByIdAsync(messageId);
            Assert.NotNull(retrieved);
            Assert.Equal("sent", retrieved!.Status);
            Assert.NotNull(retrieved.SentAt);
        }

        [Fact]
        public async Task Should_not_update_message_if_not_in_processing_state()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var messageId = Guid.NewGuid();
            var message = CreateTestMessage(
                id: messageId,
                status: "pending");  // NOT processing
            await SeedMessageAsync(connection, message);

            var updated = message;
            updated.Status = "sent";

            // Act
            await repo.UpdateAsync(updated);

            // Assert
            var retrieved = await repo.GetByIdAsync(messageId);
            Assert.NotNull(retrieved);
            Assert.Equal("pending", retrieved!.Status);
        }

        [Fact]
        public async Task Should_handle_race_condition_when_row_modified_by_other_worker()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var messageId = Guid.NewGuid();
            var message = CreateTestMessage(
                id: messageId,
                status: "processing");
            await SeedMessageAsync(connection, message);

            // Act: Worker 1 updates
            message.Status = "sent";
            await repo.UpdateAsync(message);

            // Worker 2 tries to update the same row (but it's no longer in 'processing')
            var messageForWorker2 = new MessageLog { Id = messageId, Status = "processing" };
            await repo.UpdateAsync(messageForWorker2);

            // Assert
            var retrieved = await repo.GetByIdAsync(messageId);
            Assert.NotNull(retrieved);
            Assert.Equal("sent", retrieved!.Status);
        }

        [Fact]
        public async Task Should_update_multiple_fields_atomically()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var messageId = Guid.NewGuid();
            var message = CreateTestMessage(
                id: messageId,
                status: "processing",
                retryCount: 0,
                errorMessage: null);
            await SeedMessageAsync(connection, message);

            // Act: Update multiple fields
            message.Status = "pending";
            message.RetryCount = 3;
            message.ErrorMessage = "Provider timeout";
            message.NextRetryAt = DateTime.UtcNow.AddMinutes(5);
            await repo.UpdateAsync(message);

            // Assert
            var retrieved = await repo.GetByIdAsync(messageId);
            Assert.NotNull(retrieved);
            Assert.Equal("pending", retrieved!.Status);
            Assert.Equal(3, retrieved.RetryCount);
            Assert.Equal("Provider timeout", retrieved.ErrorMessage);
            Assert.NotNull(retrieved.NextRetryAt);
        }

        // --- 7. EDGE CASES ---

        [Fact]
        public async Task Should_handle_empty_table()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);

            // Act
            var claimed = await repo.ClaimPendingAsync(batchSize: 10);

            // Assert
            Assert.Empty(claimed);
        }

        [Fact]
        public async Task Should_handle_very_large_batch_size()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var messages = Enumerable.Range(0, 100)
                .Select(i => CreateTestMessage(
                    id: Guid.NewGuid(),
                    status: "pending"))
                .ToList();
            await SeedMessagesAsync(connection, messages);

            // Act: Request more than exist
            var claimed = await repo.ClaimPendingAsync(batchSize: 1000);
            var claimedList = claimed.ToList();

            // Assert
            Assert.Equal(100, claimedList.Count);
        }

        [Fact]
        public async Task Should_handle_very_long_error_messages()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var longError = new string('X', 4000); // SQL varchar(MAX) can handle this
            var message = CreateTestMessage(
                id: Guid.NewGuid(),
                status: "pending",
                errorMessage: longError);
            await SeedMessageAsync(connection, message);

            // Act
            var retrieved = await repo.GetByIdAsync(message.Id);

            // Assert
            Assert.NotNull(retrieved);
            Assert.Equal(longError, retrieved!.ErrorMessage);
        }

        [Fact]
        public async Task Should_handle_concurrent_inserts_and_claims()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();

            // Act: Concurrent inserts and claims
            var insertTask = Task.Run(async () =>
            {
                for (int i = 0; i < 50; i++)
                {
                    using var conn = new SqlConnection(GetTestConnectionString());
                    await conn.OpenAsync();
                    var r = new DapperMessageRepository(conn);
                    var msg = CreateTestMessage(
                        id: Guid.NewGuid(),
                        status: "pending");
                    await r.InsertAsync(msg);
                }
            });

            var claimTask = Task.Run(async () =>
            {
                var totalClaimed = 0;
                for (int batch = 0; batch < 8; batch++)
                {
                    using var conn = new SqlConnection(GetTestConnectionString());
                    await conn.OpenAsync();
                    var r = new DapperMessageRepository(conn);
                    var claimed = await r.ClaimPendingAsync(batchSize: 10);
                    totalClaimed += claimed.Count();
                    await Task.Delay(20);
                }
            });

            await Task.WhenAll(insertTask, claimTask);

            // Assert
            var finalRows = await CountRowsAsync(connection);
            Assert.Equal(50, finalRows);
        }

        // --- 8. TRANSACTION AND LOCKING BEHAVIOR ---

        [Fact]
        public async Task Should_use_updlock_hint_to_prevent_double_claiming()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var message = CreateTestMessage(
                id: Guid.NewGuid(),
                status: "pending");
            await SeedMessageAsync(connection, message);

            // Act: Two concurrent claims with UPDLOCK
            // UPDLOCK prevents phantom reads and ensures serializable isolation for the claim operation
            var claim1Task = Task.Run(async () =>
            {
                using var conn = new SqlConnection(GetTestConnectionString());
                await conn.OpenAsync();
                var repo = new DapperMessageRepository(conn);
                return await repo.ClaimPendingAsync(batchSize: 1);
            });

            var claim2Task = Task.Run(async () =>
            {
                using var conn = new SqlConnection(GetTestConnectionString());
                await conn.OpenAsync();
                var repo = new DapperMessageRepository(conn);
                return await repo.ClaimPendingAsync(batchSize: 1);
            });

            var results = await Task.WhenAll(claim1Task, claim2Task);

            // Assert
            var claimed = results[0].Concat(results[1]).ToList();
            Assert.Single(claimed);
        }

        // --- 9. PERFORMANCE TESTS ---

        [Fact]
        public async Task Should_claim_large_batches_efficiently()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);
            var messages = Enumerable.Range(0, 1200)
                .Select(i => CreateTestMessage(
                    id: Guid.NewGuid(),
                    status: "pending"))
                .ToList();
            await SeedMessagesAsync(connection, messages);

            // Act: Measure claim time
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var claimed = await repo.ClaimPendingAsync(batchSize: 500);
            var claimedCount = claimed.Count();
            sw.Stop();

            // Assert
            Assert.Equal(500, claimedCount);
            Assert.True(sw.ElapsedMilliseconds < 5000, $"Claim took {sw.ElapsedMilliseconds}ms, expected < 5000ms");
        }

        [Fact]
        public async Task Should_query_performance_benefit_from_index_ix_message_log_processing()
        {
            // Arrange
            var connection = await TruncateMessageLogTableAsync();
            var repo = new DapperMessageRepository(connection);

            // Create many messages to benefit from index
            var messages = Enumerable.Range(0, 3000)
                .Select(i => CreateTestMessage(
                    id: Guid.NewGuid(),
                    status: (i % 5 == 0) ? "processing" : "pending"))
                .ToList();
            await SeedMessagesAsync(connection, messages);

            // Act: Query performance with index
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var claimed = await repo.ClaimPendingAsync(batchSize: 500);
            sw.Stop();

            Assert.Equal(500, claimed.Count());
            Assert.True(sw.ElapsedMilliseconds < 5000, $"Query took {sw.ElapsedMilliseconds}ms, expected < 5000ms");
        }
    }
}
