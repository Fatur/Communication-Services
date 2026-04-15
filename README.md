# CommunicationService

ASP.NET Core (.NET 8) Communication Service

Files added include API, worker, repository (Dapper), template (Scriban), rate limiter, circuit breaker.

Quick git setup (run locally):

```bash
# initialize repo
git init
# add files
git add .
# initial commit
git commit -m "Initial commit: communication service"
# optionally add remote
git remote add origin <your-remote-url>
git push -u origin main
```

Build & run locally:

```bash
dotnet restore
dotnet build
dotnet run
```

Create database table before running: see scripts/create_table.sql

