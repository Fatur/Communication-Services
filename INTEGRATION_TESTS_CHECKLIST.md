# Integration Tests Implementation Checklist

## Project Structure ✅

- ✅ Created `tests/CommunicationServices.IntegrationTests/` project
- ✅ Created `CommunicationServices.IntegrationTests.csproj`
- ✅ Moved test file to new project
- ✅ Removed test file from unit tests project
- ✅ Added project references to main project

---

## Database Setup

### Prerequisites
- [ ] SQL Server Express installed (or LocalDB/Docker)
- [ ] `sqlcmd` available in PATH

### Setup Steps
- [ ] Run PowerShell script: `.\scripts\create-test-db.ps1`
- [ ] Verify database created: `TestCommunicationServices`
- [ ] Verify table created: `message_log`
- [ ] Verify index created: `IX_message_log_processing`

### Connection Configuration
- [ ] Set environment variable: `TEST_DB_CONNECTION_STRING`
- [ ] Or update `appsettings.json` with connection string
- [ ] Test connection string works

---

## Test Implementation (66 Tests)

### 1. INSERT TESTS (3)
- [ ] `Should_insert_message_successfully`
- [ ] `Should_insert_multiple_messages`
- [ ] `Should_insert_message_with_null_optional_fields`

### 2. GET TESTS (3)
- [ ] `Should_return_message_by_id`
- [ ] `Should_return_null_when_message_not_found`
- [ ] `Should_map_all_columns_correctly_including_snake_case_aliases`

### 3. CLAIM PENDING TESTS (6)
- [ ] `Should_claim_pending_messages`
- [ ] `Should_respect_batch_size_limit`
- [ ] `Should_skip_messages_with_future_retry_time`
- [ ] `Should_claim_retryable_messages_when_retry_time_reached`
- [ ] `Should_claim_messages_without_next_retry_at_as_pending`

### 4. STUCK MESSAGE RECOVERY (3)
- [ ] `Should_recover_stuck_processing_messages_older_than_5_minutes`
- [ ] `Should_not_recover_recent_processing_messages_less_than_5_minutes`
- [ ] `Should_recover_multiple_stuck_messages_within_batch_size`

### 5. CONCURRENCY TESTS (4) - CRITICAL
- [ ] `Should_not_claim_same_message_twice_under_concurrent_access`
- [ ] `Should_distribute_work_between_multiple_workers_fairly`
- [ ] `Should_handle_extreme_concurrency_without_duplicates_or_corruption`
- [ ] `Should_use_readpast_to_skip_locked_rows_without_blocking`

### 6. UPDATE TESTS (5)
- [ ] `Should_update_processing_message_successfully`
- [ ] `Should_not_update_message_if_not_in_processing_state`
- [ ] `Should_handle_race_condition_when_row_modified_by_other_worker`
- [ ] `Should_update_multiple_fields_atomically`

### 7. EDGE CASES (5)
- [ ] `Should_handle_empty_table`
- [ ] `Should_handle_very_large_batch_size`
- [ ] `Should_handle_very_long_error_messages`
- [ ] `Should_handle_concurrent_inserts_and_claims`

### 8. LOCKING BEHAVIOR (1)
- [ ] `Should_use_updlock_hint_to_prevent_double_claiming`

### 9. PERFORMANCE TESTS (2)
- [ ] `Should_claim_large_batches_efficiently`
- [ ] `Should_query_performance_benefit_from_index_ix_message_log_processing`

---

## Implementation Guidelines

### For Each Test

1. **Remove `[Fact(Skip = "...")]`** and keep just `[Fact]`
2. **Uncomment assertions** in the Assert section
3. **Verify Arrange setup** creates proper test conditions
4. **Test logic** validates SQL Server behavior
5. **Run and verify** test passes

### Example Implementation

```csharp
[Fact]  // Remove Skip
public async Task Should_insert_message_successfully()
{
    // Arrange
    var connection = await TruncateMessageLogTableAsync();
    var repo = new DapperMessageRepository(connection);
    var message = CreateTestMessage(id: Guid.Parse("11111111-1111-1111-1111-111111111111"));

    // Act
    await repo.InsertAsync(message);

    // Assert - UNCOMMENT THESE
    Assert.Equal(1, await CountRowsAsync(connection));
    var retrieved = await repo.GetByIdAsync(message.Id);
    Assert.NotNull(retrieved);
    Assert.Equal(message.Id, retrieved.Id);
    Assert.Equal(message.TenantId, retrieved.TenantId);
    // ... rest of assertions
}
```

---

## Validation Checklist

### Build
- [ ] Solution builds without errors
- [ ] All projects reference correctly
- [ ] No missing NuGet packages

### Database
- [ ] Connection string works
- [ ] Table exists and is empty
- [ ] Index exists
- [ ] Can truncate table

### Tests
- [ ] Can run: `dotnet test tests/CommunicationServices.IntegrationTests/`
- [ ] Database is cleaned before each test
- [ ] Assertions uncommented and passing
- [ ] No timeout errors

### Concurrency (Critical)
- [ ] Multiple workers can claim without duplicates
- [ ] READPAST skips locked rows
- [ ] UPDLOCK prevents double-claiming
- [ ] ROWLOCK works correctly

---

## Running Tests

### Basic
```powershell
dotnet test tests/CommunicationServices.IntegrationTests/
```

### Specific Category
```powershell
# Claim tests
dotnet test --filter "DisplayName~Claim"

# Concurrency tests
dotnet test --filter "DisplayName~Concurrency"

# Performance tests
dotnet test --filter "DisplayName~Performance"
```

### Verbose
```powershell
dotnet test --verbosity detailed
```

### Watch Mode (Requires dotnet tool)
```powershell
dotnet watch test --project tests/CommunicationServices.IntegrationTests/
```

---

## Performance Expectations

| Category | Time | Notes |
|----------|------|-------|
| Insert tests | ~1s each | Fast |
| Get tests | ~1s each | Fast |
| Claim tests | ~2s each | Moderate |
| Concurrency tests | 5-10s each | Longer, multiple workers |
| Performance tests | 5-30s | Depends on data size |
| **All tests** | **2-3 min** | First run may vary |

---

## CI/CD Integration

### GitHub Actions
- [ ] Setup SQL Server service
- [ ] Create test database in workflow
- [ ] Set TEST_DB_CONNECTION_STRING environment variable
- [ ] Run: `dotnet test tests/CommunicationServices.IntegrationTests/`

### Azure DevOps
- [ ] Use SQL Server container
- [ ] Create test database
- [ ] Set environment variable
- [ ] Run tests in pipeline

### Local Development
- [ ] Run script: `.\scripts\create-test-db.ps1`
- [ ] Set env var: `$env:TEST_DB_CONNECTION_STRING = "..."`
- [ ] Run tests before commit

---

## Troubleshooting

### Connection Issues
```powershell
# Test connection
sqlcmd -S ".\SQLEXPRESS" -Q "SELECT @@VERSION"

# Verify database
sqlcmd -S ".\SQLEXPRESS" -Q "SELECT name FROM sys.databases WHERE name = 'TestCommunicationServices'"
```

### Schema Issues
```powershell
# Run script again
.\scripts\create-test-db.ps1 -Server ".\SQLEXPRESS" -Database "TestCommunicationServices"

# Or manual reset
sqlcmd -S ".\SQLEXPRESS" -d TestCommunicationServices -Q "DROP TABLE message_log"
```

### Test Failures
- [ ] Verify connection string correct
- [ ] Verify database exists and is accessible
- [ ] Check table is being truncated properly
- [ ] Look for SQL errors in output
- [ ] Verify index exists

---

## Documentation

- ✅ `tests/CommunicationServices.IntegrationTests/README.md` - Detailed setup guide
- ✅ `INTEGRATION_TESTS_SETUP.md` - Quick start
- ✅ `scripts/create-test-db.ps1` - Automated database setup
- ✅ This checklist

---

## Next Steps

1. ✅ Database setup (run script)
2. ✅ Set connection string
3. ✅ Implement INSERT tests
4. ✅ Implement GET tests
5. ✅ Implement CLAIM tests
6. ✅ Implement CONCURRENCY tests (most important!)
7. ✅ Implement UPDATE tests
8. ✅ Implement EDGE CASES
9. ✅ Implement PERFORMANCE tests
10. ✅ Add to CI/CD pipeline

---

## Support Resources

- SQL Server Locking: https://docs.microsoft.com/en-us/sql/t-sql/statements/set-transaction-isolation-level-transact-sql
- Dapper: https://github.com/DapperLib/Dapper
- xUnit: https://xunit.net/
- CTE in SQL: https://docs.microsoft.com/en-us/sql/t-sql/queries/with-common-table-expression-transact-sql

---

**Status**: ✅ Ready for implementation!
