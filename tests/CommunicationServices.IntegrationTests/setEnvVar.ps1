# Persist ke User-level (berlaku untuk semua proses baru)
[System.Environment]::SetEnvironmentVariable("TestDatabase", "Data Source=(localdb)\MSSQLLocalDB;Database=TestCommunicationServices;Trusted_Connection=True;Encrypt=true;TrustServerCertificate=True;Integrated Security=True", "User")

# SMTP environment variables sesuai dengan SmtpEmailProviderIntegrationTests.cs
[System.Environment]::SetEnvironmentVariable("SMTP_HOST", "smtp.gmail.com", "User")
[System.Environment]::SetEnvironmentVariable("SMTP_PORT", "587", "User")
[System.Environment]::SetEnvironmentVariable("SMTP_USERNAME", "fatur@inforsys.co.id", "User")
[System.Environment]::SetEnvironmentVariable("SMTP_PASSWORD", "S31panas", "User")
[System.Environment]::SetEnvironmentVariable("SMTP_SENDER_EMAIL", "fatur@inforsys.co.id", "User")
[System.Environment]::SetEnvironmentVariable("SMTP_SENDER_NAME", "My App Test", "User")
[System.Environment]::SetEnvironmentVariable("SMTP_TO_EMAIL", "yudhaputra2204@gmail.com", "User")

# Set juga di Process-level agar langsung aktif di session ini tanpa restart
$env:TestDatabase     = "Data Source=(localdb)\MSSQLLocalDB;Database=TestCommunicationServices;Trusted_Connection=True;Encrypt=true;TrustServerCertificate=True;Integrated Security=True"
$env:SMTP_HOST        = "smtp.gmail.com"
$env:SMTP_PORT        = "587"
$env:SMTP_USERNAME    = "fatur@inforsys.co.id"
$env:SMTP_PASSWORD    = "S31panas"
$env:SMTP_SENDER_EMAIL = "fatur@inforsys.co.id"
$env:SMTP_SENDER_NAME = "My App Test"
$env:SMTP_TO_EMAIL    = "yudhaputra2204@gmail.com"

Write-Host "Environment variables set (User + Process level)." -ForegroundColor Green
