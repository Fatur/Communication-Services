# Integration Tests Setup

## Quick Start

### 1. Create Test Database

**PowerShell (Recommended):**
```powershell
.\scripts\create-test-db.ps1
```

**Or Manual SQL:**
```sql
CREATE DATABASE TestCommunicationServices;

USE TestCommunicationServices;

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

### 2. Set Connection String

```powershell
$env:TEST_DB_CONNECTION_STRING = "Server=.\SQLEXPRESS;Database=TestCommunicationServices;Trusted_Connection=True;Encrypt=False;"
```

### 3. Run Tests

```powershell
dotnet test tests/CommunicationServices.IntegrationTests/
```

---

## Project Structure

```
tests/
├── CommunicationServices.UnitTests/           ← Unit tests (no database)
│   ├── MemoryCircuitBreakerTests.cs
│   ├── CommunicationServices.UnitTests.csproj
│   └── appsettings.json
│
└── CommunicationServices.IntegrationTests/    ← Integration tests (needs real DB)
    ├── DapperMessageRepositoryIntegrationTests.cs
    ├── CommunicationServices.IntegrationTests.csproj
    ├── appsettings.json
    └── README.md                              ← Full setup guide

scripts/
└── create-test-db.ps1                         ← Database setup script
```

---

## Test Categories

66 integration test skeletons covering:

✅ **Insert Tests** (3) - Row insertion  
✅ **Get Tests** (3) - Column mapping  
✅ **Claim Pending** (6) - Message claiming logic  
✅ **Stuck Message Recovery** (3) - Recovery logic  
✅ **Concurrency** (4) - No duplicate claiming under concurrent load  
✅ **Updates** (5) - State transitions  
✅ **Edge Cases** (5) - Empty table, large batches, long messages  
✅ **Locking Behavior** (1) - UPDLOCK validation  
✅ **Performance** (2) - Large batch efficiency  

---

## Running Tests

```powershell
# All integration tests
dotnet test tests/CommunicationServices.IntegrationTests/

# Specific category
dotnet test tests/CommunicationServices.IntegrationTests/ --filter "DisplayName~Claim"

# Verbose
dotnet test tests/CommunicationServices.IntegrationTests/ --verbosity detailed
```

---

## Key Features

- ✅ Real SQL Server (Express or LocalDB)
- ✅ Row-level locking (ROWLOCK, READPAST, UPDLOCK)
- ✅ Concurrency safety validation
- ✅ Automatic table cleanup between tests
- ✅ Comprehensive edge case coverage
- ✅ Performance testing with proper index

---

For detailed setup instructions, see:
- `tests/CommunicationServices.IntegrationTests/README.md`
