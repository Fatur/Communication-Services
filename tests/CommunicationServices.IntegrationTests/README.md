# Integration Tests - Database Setup Guide

## Overview

This project contains **real SQL Server integration tests** for `DapperMessageRepository`. These tests require a real database and validate:

- Actual SQL Server locking behavior (ROWLOCK, READPAST, UPDLOCK)
- Concurrency safety and race condition prevention
- Message claiming logic
- Stuck message recovery
- State transitions

⚠️ **IMPORTANT**: These are NOT unit tests. They require a real SQL Server database.

---

## Prerequisites

### 1. SQL Server Installation

Choose **ONE** of:

#### Option A: SQL Server Express (Recommended for Local Dev)
```
Download: https://www.microsoft.com/en-us/sql-server/sql-server-downloads
Choose: SQL Server 2022 Express (free, 10 GB size limit)
Installation Name: SQLEXPRESS
```

#### Option B: LocalDB (Lighter Weight)
```
Usually comes with Visual Studio
Server name: (LocalDB)\mssqllocaldb
```

#### Option C: Docker (Recommended for CI/CD)
```powershell
docker run -e "ACCEPT_EULA=Y" -e "SA_PASSWORD=YourStrongPassword123!" `
  -p 1433:1433 `
  --name sqlserver `
  -d mcr.microsoft.com/mssql/server:2022-latest
```

---

## Database Setup

### Step 1: Create Test Database

#### Via SQL Server Management Studio (SSMS)

```sql
-- Create database
CREATE DATABASE TestCommunicationServices;

-- Use the database
USE TestCommunicationServices;

-- Create message_log table
CREATE TABLE message_log (
    id UNIQUEIDENTIFIER PRIMARY KEY,
    tenant_id NVARCHAR(255) NOT NULL,
    channel NVARCHAR(50) NOT NULL,
    recipient NVARCHAR(255) NOT NULL,
    template_code NVARCHAR(100) NOT NULL,
    data_json NVARCHAR(MAX) NOT NULL,
    status NVARCHAR(50) NOT NULL DEFAULT 'pending',
    retry_count INT NOT NULL DEFAULT 0,
    error_message NVARCHAR(MAX) NULL,
    next_retry_at DATETIME2 NULL,
    processing_at DATETIME2 NULL,
    created_at DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    sent_at DATETIME2 NULL
);

-- Create recommended index for claim performance
CREATE NONCLUSTERED INDEX IX_message_log_processing
ON message_log (status, next_retry_at, processing_at, created_at)
INCLUDE (id);
```

#### Via PowerShell Script

```powershell
# scripts/create-test-db.ps1
$server = ".\SQLEXPRESS"
$database = "TestCommunicationServices"

# Create database
sqlcmd -S $server -Q "CREATE DATABASE $database;"

# Create schema
$schema = @"
USE $database;

CREATE TABLE message_log (
    id UNIQUEIDENTIFIER PRIMARY KEY,
    tenant_id NVARCHAR(255) NOT NULL,
    channel NVARCHAR(50) NOT NULL,
    recipient NVARCHAR(255) NOT NULL,
    template_code NVARCHAR(100) NOT NULL,
    data_json NVARCHAR(MAX) NOT NULL,
    status NVARCHAR(50) NOT NULL DEFAULT 'pending',
    retry_count INT NOT NULL DEFAULT 0,
    error_message NVARCHAR(MAX) NULL,
    next_retry_at DATETIME2 NULL,
    processing_at DATETIME2 NULL,
    created_at DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    sent_at DATETIME2 NULL
);

CREATE NONCLUSTERED INDEX IX_message_log_processing
ON message_log (status, next_retry_at, processing_at, created_at)
INCLUDE (id);
"@

sqlcmd -S $server -Q $schema
```

### Step 2: Verify Database Created

```sql
-- Query to verify
USE TestCommunicationServices;
SELECT COUNT(*) as TableCount FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'message_log';

-- Should return: 1
```

---

## Configure Connection String

### Option 1: Environment Variable (Recommended)

#### Windows (PowerShell)
```powershell
$env:TEST_DB_CONNECTION_STRING = "Server=.\SQLEXPRESS;Database=TestCommunicationServices;Trusted_Connection=True;Encrypt=False;"
```

#### Windows (Command Prompt)
```cmd
set TEST_DB_CONNECTION_STRING=Server=.\SQLEXPRESS;Database=TestCommunicationServices;Trusted_Connection=True;Encrypt=False;
```

#### Windows (Set Permanently)
```powershell
[System.Environment]::SetEnvironmentVariable(
    "TEST_DB_CONNECTION_STRING",
    "Server=.\SQLEXPRESS;Database=TestCommunicationServices;Trusted_Connection=True;Encrypt=False;",
    [System.EnvironmentVariableTarget]::User
)
```

#### Linux / macOS
```bash
export TEST_DB_CONNECTION_STRING="Server=.\SQLEXPRESS;Database=TestCommunicationServices;Trusted_Connection=True;Encrypt=False;"
```

### Option 2: appsettings.json

Edit `tests/CommunicationServices.IntegrationTests/appsettings.json`:

```json
{
  "ConnectionStrings": {
    "TestDatabase": "Server=.\\SQLEXPRESS;Database=TestCommunicationServices;Trusted_Connection=True;Encrypt=False;"
  }
}
```

### Connection String Examples

#### SQL Server Express (Windows Auth)
```
Server=.\SQLEXPRESS;Database=TestCommunicationServices;Trusted_Connection=True;Encrypt=False;
```

#### LocalDB (Windows Auth)
```
Server=(LocalDB)\mssqllocaldb;Database=TestCommunicationServices;Trusted_Connection=True;Encrypt=False;
```

#### Docker / SQL Server (SA Login)
```
Server=localhost,1433;Database=TestCommunicationServices;User Id=sa;Password=YourStrongPassword123!;Encrypt=False;
```

---

## Running Tests

### Run All Integration Tests

```powershell
cd D:\inforsys\notificationservices

# Run only integration tests
dotnet test tests/CommunicationServices.IntegrationTests/CommunicationServices.IntegrationTests.csproj

# Or from solution root
dotnet test --filter "FullyQualifiedName~IntegrationTests"
```

### Run Specific Test Category

```powershell
# Run only claim tests
dotnet test tests/CommunicationServices.IntegrationTests/ --filter "DisplayName~Claim"

# Run only concurrency tests
dotnet test tests/CommunicationServices.IntegrationTests/ --filter "DisplayName~Concurrency"

# Run only performance tests
dotnet test tests/CommunicationServices.IntegrationTests/ --filter "DisplayName~Performance"
```

### Run Single Test

```powershell
dotnet test tests/CommunicationServices.IntegrationTests/ `
  --filter "FullyQualifiedName=CommunicationServices.IntegrationTests.DapperMessageRepositoryIntegrationTests.Should_insert_message_successfully"
```

### Verbose Output

```powershell
dotnet test tests/CommunicationServices.IntegrationTests/ --verbosity detailed
```

---

## Database Cleanup

Each test calls `TruncateMessageLogTableAsync()` to clean the table before testing.

### Manual Cleanup Between Test Runs

```sql
USE TestCommunicationServices;
TRUNCATE TABLE message_log;
```

### Full Database Reset

```sql
-- Drop and recreate
DROP TABLE message_log;

CREATE TABLE message_log (
    id UNIQUEIDENTIFIER PRIMARY KEY,
    tenant_id NVARCHAR(255) NOT NULL,
    channel NVARCHAR(50) NOT NULL,
    recipient NVARCHAR(255) NOT NULL,
    template_code NVARCHAR(100) NOT NULL,
    data_json NVARCHAR(MAX) NOT NULL,
    status NVARCHAR(50) NOT NULL DEFAULT 'pending',
    retry_count INT NOT NULL DEFAULT 0,
    error_message NVARCHAR(MAX) NULL,
    next_retry_at DATETIME2 NULL,
    processing_at DATETIME2 NULL,
    created_at DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    sent_at DATETIME2 NULL
);

CREATE NONCLUSTERED INDEX IX_message_log_processing
ON message_log (status, next_retry_at, processing_at, created_at)
INCLUDE (id);
```

---

## Troubleshooting

### Error: "Login failed for user"

**Cause**: Wrong connection string or SQL Server not running

**Solution**:
```powershell
# Check if SQL Server is running
Get-Service -Name MSSQL$SQLEXPRESS | Select-Object Status

# If stopped, start it
Start-Service -Name MSSQL$SQLEXPRESS
```

### Error: "Cannot open database 'TestCommunicationServices'"

**Cause**: Database doesn't exist

**Solution**:
```sql
-- Create it via SSMS or run the setup script again
CREATE DATABASE TestCommunicationServices;
```

### Error: "TRUNCATE TABLE failed because of a FOREIGN KEY"

**Cause**: Foreign key constraints on table

**Solution**:
```sql
-- Disable constraint temporarily
ALTER TABLE message_log NOCHECK CONSTRAINT ALL;
TRUNCATE TABLE message_log;
ALTER TABLE message_log CHECK CONSTRAINT ALL;
```

### Error: "Timeout expired"

**Cause**: Long-running tests or slow database

**Solution**:
- Increase `CommandTimeoutSeconds` in repository
- Check database performance
- Create missing indexes

---

## CI/CD Pipeline

### GitHub Actions Example

```yaml
name: Integration Tests

on: [push, pull_request]

jobs:
  integration-tests:
    runs-on: windows-latest

    services:
      mssql:
        image: mcr.microsoft.com/mssql/server:2022-latest
        env:
          ACCEPT_EULA: Y
          SA_PASSWORD: TestPassword123!
        options: >-
          --health-cmd="/opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P TestPassword123! -Q 'SELECT 1'"
          --health-interval 10s
          --health-timeout 5s
          --health-retries 5
        ports:
          - 1433:1433

    steps:
      - uses: actions/checkout@v3

      - name: Setup .NET
        uses: actions/setup-dotnet@v3
        with:
          dotnet-version: '8.0'

      - name: Create test database
        run: |
          sqlcmd -S localhost -U sa -P TestPassword123! -Q "CREATE DATABASE TestCommunicationServices;"

      - name: Run integration tests
        env:
          TEST_DB_CONNECTION_STRING: "Server=localhost;Database=TestCommunicationServices;User Id=sa;Password=TestPassword123!;Encrypt=False;"
        run: dotnet test tests/CommunicationServices.IntegrationTests/
```

---

## Performance Expectations

### Test Execution Time

- **Individual test**: 1-5 seconds
- **Insert tests**: ~1 second
- **Get tests**: ~1 second
- **Claim tests**: ~2 seconds
- **Concurrency tests**: 5-10 seconds
- **All tests**: ~2-3 minutes

### With Proper Index

```sql
-- Should benefit query performance
CREATE NONCLUSTERED INDEX IX_message_log_processing
ON message_log (status, next_retry_at, processing_at, created_at)
INCLUDE (id);
```

---

## Best Practices

1. ✅ Run integration tests in separate project (done)
2. ✅ Use real database (not in-memory)
3. ✅ Clean up data before each test
4. ✅ Use reasonable batch sizes for seeding
5. ✅ Test locking behavior with real SQL transactions
6. ✅ Monitor for deadlocks
7. ✅ Use connection pooling
8. ✅ Measure performance with proper index

---

## Next Steps

1. ✅ Create test database (see Step 1)
2. ✅ Set environment variable or appsettings.json
3. ✅ Verify connection string works
4. ✅ Run first test: `dotnet test --filter "DisplayName~Should_insert_message_successfully"`
5. ✅ Implement remaining tests as needed
6. ✅ Add to CI/CD pipeline

---

## Support

For issues:
1. Check database exists: `SELECT name FROM sys.databases`
2. Verify SQL Server running: `sqlcmd -S . -Q "SELECT @@VERSION"`
3. Check connection string in logs
4. Verify index created: `SELECT name FROM sys.indexes WHERE object_id = OBJECT_ID('message_log')`
