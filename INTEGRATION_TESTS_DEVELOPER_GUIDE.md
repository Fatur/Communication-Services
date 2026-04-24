# Integration Tests - Developer Start Guide

Welcome! 👋 Berikut panduan untuk memulai dengan Integration Tests untuk DapperMessageRepository.

---

## Apa Itu Integration Tests?

Integration tests **BERBEDA** dari unit tests:

| Aspek | Unit Tests | Integration Tests |
|-------|-----------|------------------|
| **Database** | ❌ Tidak perlu | ✅ Real SQL Server |
| **Kecepatan** | ⚡ Cepat (< 1 detik) | 🐢 Lambat (2-3 menit) |
| **Setup** | Minimal | Kompleks (DB, schema) |
| **Fokus** | Logic, behavior | Real DB, locking, concurrency |
| **Lokasi** | `tests/CommunicationServices.UnitTests/` | `tests/CommunicationServices.IntegrationTests/` |

---

## Mengapa Dipisah?

✅ **Developer experience lebih baik**
- Unit tests bisa run cepat (< 1 menit)
- Integration tests bisa run lambat (2-3 menit)

✅ **CI/CD lebih fleksibel**
- Local dev: jalankan unit tests saja
- Build server: jalankan semua tests

✅ **Infrastructure clarity**
- Jelas mana yang butuh database
- Jelas mana yang bisa offline

---

## Setup Awal (5 menit)

### 1. Jalankan Database Setup Script

```powershell
# Dari root folder solution
.\scripts\create-test-db.ps1
```

Script ini akan:
- ✅ Membuat database `TestCommunicationServices`
- ✅ Membuat tabel `message_log`
- ✅ Membuat index `IX_message_log_processing`

### 2. Set Connection String

```powershell
$env:TEST_DB_CONNECTION_STRING = "Server=.\SQLEXPRESS;Database=TestCommunicationServices;Trusted_Connection=True;Encrypt=False;"
```

### 3. Verifikasi Setup

```powershell
# Jalankan test pertama
dotnet test tests/CommunicationServices.IntegrationTests/ --filter "DisplayName~Should_insert_message_successfully"
```

---

## Implementasi Tests

### Struktur Setiap Test

```csharp
[Fact]  // ← Remove Skip dan keep Fact
public async Task Should_insert_message_successfully()
{
    // 1. ARRANGE - Setup test data
    var connection = await TruncateMessageLogTableAsync();
    var repo = new DapperMessageRepository(connection);
    var message = CreateTestMessage(...);

    // 2. ACT - Execute operation
    await repo.InsertAsync(message);

    // 3. ASSERT - Verify behavior
    // Assert.Equal(1, await CountRowsAsync(connection));
    // var retrieved = await repo.GetByIdAsync(message.Id);
    // Assert.NotNull(retrieved);
}
```

### Cara Implement

1. **Buka test skeleton** di `DapperMessageRepositoryIntegrationTests.cs`
2. **Hapus** `[Fact(Skip = "...")]`
3. **Uncomment** semua assertions di bagian Assert
4. **Run** test: `dotnet test --filter "DisplayName~TestName"`
5. **Verifikasi** test PASS ✅

---

## Running Tests

### All Tests
```powershell
dotnet test tests/CommunicationServices.IntegrationTests/
```

### Specific Category
```powershell
# Insert tests
dotnet test --filter "DisplayName~insert"

# Claim tests  
dotnet test --filter "DisplayName~Claim"

# Concurrency tests (CRITICAL)
dotnet test --filter "DisplayName~concurrent"
```

### Single Test
```powershell
dotnet test --filter "DisplayName~Should_insert_message_successfully"
```

### Verbose Output
```powershell
dotnet test tests/CommunicationServices.IntegrationTests/ --verbosity detailed
```

---

## Test Categories (66 Tests)

### 1. INSERT TESTS (3)
Implementasi dulu! Paling simple.
```
□ Should_insert_message_successfully
□ Should_insert_multiple_messages
□ Should_insert_message_with_null_optional_fields
```

### 2. GET TESTS (3)
```
□ Should_return_message_by_id
□ Should_return_null_when_message_not_found
□ Should_map_all_columns_correctly_including_snake_case_aliases
```

### 3. CLAIM PENDING TESTS (6)
```
□ Should_claim_pending_messages
□ Should_respect_batch_size_limit
□ Should_skip_messages_with_future_retry_time
□ Should_claim_retryable_messages_when_retry_time_reached
□ Should_claim_messages_without_next_retry_at_as_pending
```

### 4. STUCK RECOVERY (3)
```
□ Should_recover_stuck_processing_messages_older_than_5_minutes
□ Should_not_recover_recent_processing_messages_less_than_5_minutes
□ Should_recover_multiple_stuck_messages_within_batch_size
```

### 5. CONCURRENCY TESTS (4) ⭐ CRITICAL!
**PALING PENTING** untuk production safety!
```
□ Should_not_claim_same_message_twice_under_concurrent_access
□ Should_distribute_work_between_multiple_workers_fairly
□ Should_handle_extreme_concurrency_without_duplicates_or_corruption
□ Should_use_readpast_to_skip_locked_rows_without_blocking
```

### 6. UPDATE TESTS (5)
```
□ Should_update_processing_message_successfully
□ Should_not_update_message_if_not_in_processing_state
□ Should_handle_race_condition_when_row_modified_by_other_worker
□ Should_update_multiple_fields_atomically
```

### 7. EDGE CASES (5)
```
□ Should_handle_empty_table
□ Should_handle_very_large_batch_size
□ Should_handle_very_long_error_messages
□ Should_handle_concurrent_inserts_and_claims
```

### 8. LOCKING BEHAVIOR (1)
```
□ Should_use_updlock_hint_to_prevent_double_claiming
```

### 9. PERFORMANCE (2)
```
□ Should_claim_large_batches_efficiently
□ Should_query_performance_benefit_from_index_ix_message_log_processing
```

---

## Recommended Implementation Order

1. ✅ **INSERT tests** (simple, basic)
2. ✅ **GET tests** (simple, basic)
3. ✅ **CLAIM tests** (moderate, core logic)
4. ✅ **RECOVERY tests** (moderate)
5. ✅ **UPDATE tests** (important)
6. ✅ **EDGE CASES** (validation)
7. ✅ **CONCURRENCY** (critical, may be complex)
8. ✅ **LOCKING** (advanced)
9. ✅ **PERFORMANCE** (optimization)

---

## Common Scenarios

### Scenario 1: Implementing First Test

```powershell
# 1. Open file
code tests/CommunicationServices.IntegrationTests/DapperMessageRepositoryIntegrationTests.cs

# 2. Find test
# Search: "Should_insert_message_successfully"

# 3. Remove Skip
# Change: [Fact(Skip = "...")]
# To:     [Fact]

# 4. Uncomment assertions
# Uncomment all Assert.* lines

# 5. Run test
dotnet test --filter "DisplayName~Should_insert_message_successfully"

# 6. Should see: PASSED ✅
```

### Scenario 2: Test Fails

```powershell
# 1. Run dengan verbose
dotnet test --filter "DisplayName~TestName" --verbosity detailed

# 2. Check error message
# Cek apakah assertion yang salah atau database issue

# 3. Common issues:
# - Connection string salah → verify env variable
# - Database tidak ada → run create-test-db.ps1 lagi
# - Table tidak ada → check manual dengan SSMS
# - Data tidak inserted → check InsertAsync works first
```

### Scenario 3: Concurrency Test Complexity

Jika concurrency test terasa kompleks:

```csharp
// Start simple - sequential version
[Fact]
public async Task Should_claim_pending_messages_sequential()
{
    // Arrange
    var connection = await TruncateMessageLogTableAsync();
    var repo = new DapperMessageRepository(connection);
    var messages = Enumerable.Range(0, 5)
        .Select(i => CreateTestMessage(status: "pending"))
        .ToList();
    await SeedMessagesAsync(connection, messages);

    // Act
    var claimed = await repo.ClaimPendingAsync(batchSize: 5);

    // Assert
    Assert.Equal(5, claimed.Count());
}
```

Then add concurrency...

---

## Troubleshooting

### ❌ "Database not found"
```powershell
# Solution: Run setup script
.\scripts\create-test-db.ps1

# Or verify manually
sqlcmd -S ".\SQLEXPRESS" -Q "SELECT name FROM sys.databases"
```

### ❌ "Connection timeout"
```powershell
# 1. Check SQL Server running
Get-Service -Name MSSQL$SQLEXPRESS

# 2. Verify connection string
$env:TEST_DB_CONNECTION_STRING

# 3. Test manually
sqlcmd -S ".\SQLEXPRESS" -Q "SELECT @@VERSION"
```

### ❌ "Test always skipped"
```
Make sure you:
1. Removed [Fact(Skip = "...")]
2. Keep [Fact]
3. Re-build project
```

### ❌ "Assertion fails but data looks correct"
```powershell
# Check database manually
sqlcmd -S ".\SQLEXPRESS" -d TestCommunicationServices -Q "SELECT COUNT(*) FROM message_log"

# Check exact values
sqlcmd -S ".\SQLEXPRESS" -d TestCommunicationServices -Q "SELECT TOP 5 * FROM message_log"
```

---

## Tips & Tricks

### 1. Run Tests Frequently
```powershell
# While developing - run after each change
dotnet test --filter "DisplayName~YourTest" --verbosity normal
```

### 2. Use Verbose for Debugging
```powershell
# When test fails
dotnet test --filter "DisplayName~YourTest" --verbosity detailed
```

### 3. Test Categories
```powershell
# Group by area
dotnet test --filter "DisplayName~Claim"          # All claim tests
dotnet test --filter "DisplayName~concurrent"     # All concurrency
```

### 4. Clean Database
```powershell
# If data corruption
sqlcmd -S ".\SQLEXPRESS" -d TestCommunicationServices -Q "TRUNCATE TABLE message_log"
```

### 5. Performance Monitoring
```csharp
// Add stopwatch to measure
var sw = System.Diagnostics.Stopwatch.StartNew();
var claimed = await repo.ClaimPendingAsync(batchSize: 1000);
sw.Stop();
Console.WriteLine($"Claim time: {sw.ElapsedMilliseconds}ms");
```

---

## Before Committing

```powershell
# 1. Run all tests
dotnet test tests/CommunicationServices.IntegrationTests/

# 2. Check build
dotnet build

# 3. Verify no skipped tests
dotnet test --filter "DisplayName~Skip"

# 4. Run unit tests too (ensure not broken)
dotnet test tests/CommunicationServices.UnitTests/

# 5. Commit! ✅
```

---

## Documentation References

- **Quick Start**: `INTEGRATION_TESTS_SETUP.md`
- **Detailed Setup**: `tests/CommunicationServices.IntegrationTests/README.md`
- **Implementation Guide**: `INTEGRATION_TESTS_CHECKLIST.md`
- **Command Reference**: `INTEGRATION_TESTS_QUICK_REFERENCE.md`

---

## Questions?

Check:
1. README in `tests/CommunicationServices.IntegrationTests/`
2. Inline test comments (Arrange/Act/Assert)
3. Helper methods like `CreateTestMessage()`, `SeedMessageAsync()`

---

## Next Step

👉 **Run**: `.\scripts\create-test-db.ps1`

Then: `dotnet test tests/CommunicationServices.IntegrationTests/ --filter "DisplayName~Should_insert_message_successfully"`

Good luck! 🚀
