# Script to setup test database for integration tests
# Usage: .\create-test-db.ps1 [-Server ".\SQLEXPRESS"] [-Database "TestCommunicationServices"]

# Script to setup test database for integration tests
# Usage: .\create-test-db.ps1 [-Server '.\SQLEXPRESS'] [-Database 'TestCommunicationServices']

param(
    [string]$Server   = '.\SQLEXPRESS',
    [string]$Database = 'TestCommunicationServices'
)

$ErrorActionPreference = 'Stop'

Write-Host '============================================' -ForegroundColor Cyan
Write-Host 'SQL Server Test Database Setup'              -ForegroundColor Cyan
Write-Host '============================================' -ForegroundColor Cyan
Write-Host ''

# [1/4] Check sqlcmd
Write-Host '[1/4] Checking for sqlcmd...' -ForegroundColor Yellow
if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    Write-Host 'sqlcmd not found. Install SQL Server command-line tools.' -ForegroundColor Red
    exit 1
}
Write-Host 'sqlcmd found' -ForegroundColor Green

# [2/4] Test connection  (use single-quoted string so @@ is never interpolated)
Write-Host "[2/4] Testing connection to '$Server'..." -ForegroundColor Yellow
sqlcmd -S $Server -Q 'SELECT @@VERSION' -h -1 | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Cannot connect to SQL Server '$Server'" -ForegroundColor Red
    exit 1
}
Write-Host 'Connected to SQL Server' -ForegroundColor Green

# [3/4] Create database
Write-Host "[3/4] Creating database '$Database'..." -ForegroundColor Yellow
# Build SQL as a single string using string format to avoid here-string issues
$createDb = ("IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = '{0}') " +
             "CREATE DATABASE [{0}];" ) -f $Database
sqlcmd -S $Server -Q $createDb | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Failed to create database '$Database'" -ForegroundColor Red
    exit 1
}
Write-Host "Database '$Database' ready" -ForegroundColor Green

# [4/4] Create schema using a literal here-string (@'...'@) — no variable expansion,
#       no @@ conflicts, no SQL comment parsing issues.
Write-Host '[4/4] Creating schema...' -ForegroundColor Yellow

$dropTable = "IF OBJECT_ID('message_log', 'U') IS NOT NULL DROP TABLE message_log;"
sqlcmd -S $Server -d $Database -Q $dropTable | Out-Null

# Literal here-string: PowerShell never interprets content inside @'...'@
$createSchema = @'
CREATE TABLE message_log (
    id             UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    tenant_id      VARCHAR(50)      NOT NULL,
    channel        VARCHAR(20)      NOT NULL,
    recipient      VARCHAR(255)     NOT NULL,
    template_code  VARCHAR(100)     NOT NULL,
    data_json      NVARCHAR(MAX)    NOT NULL,
    status         VARCHAR(20)      NOT NULL,
    retry_count    INT              NOT NULL DEFAULT 0,
    error_message  NVARCHAR(MAX)    NULL,
    next_retry_at  DATETIME         NULL,
    processing_at  DATETIME         NULL,
    created_at     DATETIME         NOT NULL,
    sent_at        DATETIME         NULL
);
CREATE INDEX IX_message_log_status_created_at
    ON message_log (status, created_at);
CREATE NONCLUSTERED INDEX IX_message_log_processing
    ON message_log (status, next_retry_at, processing_at, created_at)
    INCLUDE (id);
'@

sqlcmd -S $Server -d $Database -Q $createSchema | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host 'Failed to create schema' -ForegroundColor Red
    exit 1
}
Write-Host 'Schema created successfully' -ForegroundColor Green

$connStr = "Server=$Server;Database=$Database;Trusted_Connection=True;TrustServerCertificate=True;"

Write-Host ''
Write-Host '============================================' -ForegroundColor Green
Write-Host 'Setup Complete!'                             -ForegroundColor Green
Write-Host '============================================' -ForegroundColor Green
Write-Host ''
Write-Host 'Connection string:' -ForegroundColor Cyan
Write-Host "  $connStr"         -ForegroundColor Gray
Write-Host ''
Write-Host 'Set env var then run tests:' -ForegroundColor Cyan
Write-Host "  `$env:TEST_DB_CONNECTION_STRING = '$connStr'" -ForegroundColor Gray
Write-Host '  dotnet test tests/CommunicationServices.IntegrationTests/'  -ForegroundColor Gray
Write-Host ''
