[System.Environment]::SetEnvironmentVariable("TestDatabase", "Data Source=DESKTOP-MRHO6K0\SQLEXPRESS;Database=TestCommunicationServices;Trusted_Connection=True;Encrypt=true;TrustServerCertificate=True;Integrated Security=True", "User")

# SMTP environment variables sesuai dengan SmtpEmailProviderIntegrationTests.cs
[System.Environment]::SetEnvironmentVariable("SMTP_HOST", "smtp.gmail.com", "User")
[System.Environment]::SetEnvironmentVariable("SMTP_PORT", "587", "User")
[System.Environment]::SetEnvironmentVariable("SMTP_USERNAME", "fatur@inforsys.co.id", "User")
[System.Environment]::SetEnvironmentVariable("SMTP_PASSWORD", "S31panas", "User")
[System.Environment]::SetEnvironmentVariable("SMTP_SENDER_EMAIL", "fatur@inforsys.co.id", "User")
[System.Environment]::SetEnvironmentVariable("SMTP_SENDER_NAME", "My App Test", "User")
[System.Environment]::SetEnvironmentVariable("SMTP_TO_EMAIL", "mfathur@gmail.com", "User")
