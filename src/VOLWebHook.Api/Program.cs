using VOLWebHook.Api.Configuration;
using VOLWebHook.Api.Logging;
using VOLWebHook.Api.Middleware;
using VOLWebHook.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.Configure<WebhookSettings>(
    builder.Configuration.GetSection(WebhookSettings.SectionName));
builder.Services.Configure<SecuritySettings>(
    builder.Configuration.GetSection(SecuritySettings.SectionName));
builder.Services.Configure<LoggingSettings>(
    builder.Configuration.GetSection(LoggingSettings.SectionName));

// Logging
var loggingSettings = builder.Configuration
    .GetSection(LoggingSettings.SectionName)
    .Get<LoggingSettings>() ?? new LoggingSettings();

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddRollingFile(loggingSettings);

// Services
builder.Services.AddSingleton<IWebhookPersistenceService, FileWebhookPersistenceService>();
builder.Services.AddScoped<IWebhookProcessingService, WebhookProcessingService>();

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

// Configure Kestrel for production
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    var webhookSettings = builder.Configuration
        .GetSection(WebhookSettings.SectionName)
        .Get<WebhookSettings>() ?? new WebhookSettings();

    serverOptions.Limits.MaxRequestBodySize = webhookSettings.MaxPayloadSizeBytes;
});

var app = builder.Build();

// Middleware pipeline - order matters
app.UseMiddleware<RequestBufferingMiddleware>();
app.UseMiddleware<IpAllowlistMiddleware>();
app.UseMiddleware<ApiKeyAuthenticationMiddleware>();
app.UseMiddleware<HmacSignatureMiddleware>();
app.UseMiddleware<RateLimitingMiddleware>();

app.UseRouting();

app.MapControllers();
app.MapHealthChecks("/health");

// Startup logging
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var env = app.Environment.EnvironmentName;
logger.LogInformation("VOLWebHook service starting in {Environment} mode", env);
logger.LogInformation("Webhook endpoint: POST /webhook");
logger.LogInformation("Health check endpoint: GET /health");

app.Run();
