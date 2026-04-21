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

var builder = WebApplication.CreateBuilder(args);

// Configuration
var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Server=localhost;Database=commservice;Trusted_Connection=True;";

// Services
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// DB connection factory
builder.Services.AddTransient<IDbConnection>(sp => new SqlConnection(connectionString));

// Repositories / infrastructure
builder.Services.AddScoped<IMessageRepository, DapperMessageRepository>();

// Providers
builder.Services.AddSingleton<IEmailProvider, EmailProvider>();
builder.Services.AddSingleton<IWhatsAppProvider, WhatsAppProvider>();

// Template
builder.Services.AddSingleton<ITemplateService, TemplateService>();

// Handlers
builder.Services.AddSingleton<EmailHandler>();
builder.Services.AddSingleton<WhatsAppHandler>();

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
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "CommunicationService v1"));
}

app.MapControllers();

app.Run();
