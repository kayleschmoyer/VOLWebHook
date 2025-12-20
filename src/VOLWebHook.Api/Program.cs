using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using VOLWebHook.Api.Configuration;
using VOLWebHook.Api.Logging;
using VOLWebHook.Api.Middleware;
using VOLWebHook.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuration
var webhookSettings = builder.Configuration
    .GetSection(WebhookSettings.SectionName)
    .Get<WebhookSettings>() ?? new WebhookSettings();
var securitySettings = builder.Configuration
    .GetSection(SecuritySettings.SectionName)
    .Get<SecuritySettings>() ?? new SecuritySettings();
var loggingSettings = builder.Configuration
    .GetSection(LoggingSettings.SectionName)
    .Get<LoggingSettings>() ?? new LoggingSettings();

builder.Services.Configure<WebhookSettings>(
    builder.Configuration.GetSection(WebhookSettings.SectionName));
builder.Services.Configure<SecuritySettings>(
    builder.Configuration.GetSection(SecuritySettings.SectionName));
builder.Services.Configure<LoggingSettings>(
    builder.Configuration.GetSection(LoggingSettings.SectionName));

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddRollingFile(loggingSettings);

// Services
builder.Services.AddSingleton<IWebhookPersistenceService, FileWebhookPersistenceService>();
builder.Services.AddScoped<IWebhookProcessingService, WebhookProcessingService>();

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

// Configure Kestrel
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = webhookSettings.MaxPayloadSizeBytes;
});

var app = builder.Build();

// Serve static files (UI)
app.UseDefaultFiles();
app.UseStaticFiles();

// Middleware pipeline - only apply security middleware to webhook endpoint
app.UseWhen(context => context.Request.Path.StartsWithSegments("/webhook"), appBuilder =>
{
    appBuilder.UseMiddleware<RequestBufferingMiddleware>();
    appBuilder.UseMiddleware<IpAllowlistMiddleware>();
    appBuilder.UseMiddleware<ApiKeyAuthenticationMiddleware>();
    appBuilder.UseMiddleware<HmacSignatureMiddleware>();
    appBuilder.UseMiddleware<RateLimitingMiddleware>();
});

app.UseRouting();

app.MapControllers();

// Fallback to index.html for SPA routing
app.MapFallbackToFile("index.html");

// Health check with detailed response
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            service = "VOLWebHook",
            environment = app.Environment.EnvironmentName,
            timestamp = DateTime.UtcNow,
            uptime = DateTime.UtcNow - Program.StartTime,
            security = new
            {
                ipAllowlist = securitySettings.IpAllowlist.Enabled,
                apiKey = securitySettings.ApiKey.Enabled,
                hmac = securitySettings.Hmac.Enabled,
                rateLimit = securitySettings.RateLimit.Enabled
            }
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
});

// Startup banner
var logger = app.Services.GetRequiredService<ILogger<Program>>();
Program.StartTime = DateTime.UtcNow;

Console.WriteLine();
Console.WriteLine("  ╔═══════════════════════════════════════════════════════════╗");
Console.WriteLine("  ║                                                           ║");
Console.WriteLine("  ║   ██╗   ██╗ ██████╗ ██╗    ██╗    ██╗███████╗██████╗      ║");
Console.WriteLine("  ║   ██║   ██║██╔═══██╗██║    ██║    ██║██╔════╝██╔══██╗     ║");
Console.WriteLine("  ║   ██║   ██║██║   ██║██║    ██║ █╗ ██║█████╗  ██████╔╝     ║");
Console.WriteLine("  ║   ╚██╗ ██╔╝██║   ██║██║    ██║███╗██║██╔══╝  ██╔══██╗     ║");
Console.WriteLine("  ║    ╚████╔╝ ╚██████╔╝███████╗╚███╔███╔╝███████╗██████╔╝     ║");
Console.WriteLine("  ║     ╚═══╝   ╚═════╝ ╚══════╝ ╚══╝╚══╝ ╚══════╝╚═════╝      ║");
Console.WriteLine("  ║                                                           ║");
Console.WriteLine("  ║           Enterprise Webhook Management System            ║");
Console.WriteLine("  ║                                                           ║");
Console.WriteLine("  ╚═══════════════════════════════════════════════════════════╝");
Console.WriteLine();
Console.WriteLine($"  Environment:  {app.Environment.EnvironmentName}");
Console.WriteLine($"  Dashboard:    http://localhost:5000  (UI)");
Console.WriteLine($"  Webhook:      POST /webhook");
Console.WriteLine($"  Health:       GET /health");
Console.WriteLine($"  API:          GET /api/ui/*");
Console.WriteLine();
Console.WriteLine($"  Storage:      {webhookSettings.PayloadStoragePath}");
Console.WriteLine($"  Max Payload:  {webhookSettings.MaxPayloadSizeBytes / 1024 / 1024} MB");
Console.WriteLine($"  Retention:    {webhookSettings.PayloadRetentionDays} days");
Console.WriteLine();
Console.WriteLine("  Security Features:");
Console.WriteLine($"    ├─ IP Allowlist: {(securitySettings.IpAllowlist.Enabled ? "✓ ENABLED" : "○ disabled")}");
Console.WriteLine($"    ├─ API Key:      {(securitySettings.ApiKey.Enabled ? "✓ ENABLED" : "○ disabled")}");
Console.WriteLine($"    ├─ HMAC:         {(securitySettings.Hmac.Enabled ? "✓ ENABLED" : "○ disabled")}");
Console.WriteLine($"    └─ Rate Limit:   {(securitySettings.RateLimit.Enabled ? "✓ ENABLED" : "○ disabled")}");
Console.WriteLine();
Console.WriteLine("  ─────────────────────────────────────────────────────────────");
Console.WriteLine();

logger.LogInformation("VOLWebHook started successfully");

app.Run();

public partial class Program
{
    public static DateTime StartTime { get; set; }
}
