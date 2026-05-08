using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CommunicationServices.Application.Interfaces;
using CommunicationServices.Infrastructure.Data;
using CommunicationServices.Infrastructure.Providers;
using CommunicationServices.Infrastructure.Templates;
using Microsoft.OpenApi.Models;
using CommunicationServices.Worker;
using CommunicationServices.Application.Handlers;
using CommunicationServices.Infrastructure.Rates;
using CommunicationServices.Infrastructure.Circuit;
using Microsoft.Extensions.Caching.Memory;
using CommunicationServices.Infrastructure.Factory;

var builder = WebApplication.CreateBuilder(args);

// Configuration
var comConnectionString = builder.Configuration.GetConnectionString("Default");
CommunicationServices.Helper.Encryptor.Initialize(builder.Configuration);

// Services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

// DB connection factory
// IMPORTANT: IDbConnection must be registered as Scoped, NOT Singleton.
// A scoped registration ensures a single IDbConnection instance is used per DI scope (e.g. per request
// or per worker scope) which preserves correct connection lifecycle and avoids sharing a single
// connection across concurrently executing operations.
builder.Services.AddScoped<IDbConnection>(sp => new SqlConnection(comConnectionString));
builder.Services.AddSingleton<IConnectionFactory, ConnectionFactory>();

// Repositories / infrastructure
builder.Services.AddScoped<IMessageRepository, DapperMessageRepository>();

// Providers
builder.Services.AddSingleton<IEmailProvider, EmailProvider>();
builder.Services.AddSingleton<IWhatsAppProvider, WhatsAppProvider>();

// Template
builder.Services.AddSingleton<ITemplateService, TemplateService>();

// Handlers
builder.Services.AddScoped<EmailHandler>();
builder.Services.AddScoped<WhatsAppHandler>();

// Rate limiter & circuit breaker
builder.Services.AddSingleton<IRateLimiter, MemoryRateLimiter>();
builder.Services.AddSingleton<ICircuitBreaker, MemoryCircuitBreaker>();

// Worker
builder.Services.AddHostedService<MessageProcessorService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "CommunicationServices v1"));
}

app.MapControllers();

app.Run();



