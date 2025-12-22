using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using VOLWebHook.Api.Configuration;
using VOLWebHook.Api.Logging;
using VOLWebHook.Api.Middleware;
using VOLWebHook.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ============================================================================
// CONFIGURATION
// ============================================================================

// Clear default sources and add in priority order:
// 1. appsettings.json (base)
// 2. appsettings.{Environment}.json (environment-specific overrides)
// 3. Environment variables (highest priority - for secrets)
builder.Configuration.Sources.Clear();
builder.Configuration
    .SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables(prefix: "VOLWEBHOOK_");

// Bind configuration sections
builder.Services.Configure<WebhookSettings>(
    builder.Configuration.GetSection(WebhookSettings.SectionName));
builder.Services.Configure<SecuritySettings>(
    builder.Configuration.GetSection(SecuritySettings.SectionName));
builder.Services.Configure<LoggingSettings>(
    builder.Configuration.GetSection(LoggingSettings.SectionName));
builder.Services.Configure<DashboardSettings>(
    builder.Configuration.GetSection(DashboardSettings.SectionName));
builder.Services.Configure<ForwardedHeadersConfig>(
    builder.Configuration.GetSection(ForwardedHeadersConfig.SectionName));

// ============================================================================
// STARTUP VALIDATION
// ============================================================================

var webhookSettings = builder.Configuration
    .GetSection(WebhookSettings.SectionName)
    .Get<WebhookSettings>() ?? new WebhookSettings();
var securitySettings = builder.Configuration
    .GetSection(SecuritySettings.SectionName)
    .Get<SecuritySettings>() ?? new SecuritySettings();
var loggingSettings = builder.Configuration
    .GetSection(LoggingSettings.SectionName)
    .Get<LoggingSettings>() ?? new LoggingSettings();
var dashboardSettings = builder.Configuration
    .GetSection(DashboardSettings.SectionName)
    .Get<DashboardSettings>() ?? new DashboardSettings();
var forwardedHeadersConfig = builder.Configuration
    .GetSection(ForwardedHeadersConfig.SectionName)
    .Get<ForwardedHeadersConfig>() ?? new ForwardedHeadersConfig();

// Validate critical configuration at startup
ValidateConfiguration(webhookSettings, securitySettings, dashboardSettings, builder.Environment);

// ============================================================================
// LOGGING
// ============================================================================

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.FormatterName = "simple";
});
builder.Logging.AddRollingFile(loggingSettings);

// Set minimum log level based on environment
if (builder.Environment.IsProduction())
{
    builder.Logging.SetMinimumLevel(LogLevel.Information);
}
else
{
    builder.Logging.SetMinimumLevel(LogLevel.Debug);
}

// ============================================================================
// SERVICES
// ============================================================================

builder.Services.AddSingleton<IWebhookPersistenceService, FileWebhookPersistenceService>();
builder.Services.AddScoped<IWebhookProcessingService, WebhookProcessingService>();
builder.Services.AddSingleton<IConfigurationWriterService, ConfigurationWriterService>();
builder.Services.AddControllers();

// Health checks with storage validation
builder.Services.AddHealthChecks()
    .AddCheck("storage", () =>
    {
        try
        {
            var storagePath = webhookSettings.PayloadStoragePath;
            if (!Directory.Exists(storagePath))
            {
                Directory.CreateDirectory(storagePath);
            }
            // Test write permissions
            var testFile = Path.Combine(storagePath, ".health_check");
            File.WriteAllText(testFile, DateTime.UtcNow.ToString());
            File.Delete(testFile);
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Storage is accessible and writable");
        }
        catch (Exception ex)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Storage is not accessible", ex);
        }
    });

// ============================================================================
// CORS CONFIGURATION (Restrictive in Production)
// ============================================================================

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("DevelopmentCors", policy =>
        {
            policy.WithOrigins("http://localhost:3000", "http://localhost:5000", "https://localhost:5001")
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        });
    });
}
else
{
    // Production: CORS should be configured explicitly with allowed origins
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("ProductionCors", policy =>
        {
            // Allow same-origin only by default
            policy.WithOrigins($"https://{Environment.GetEnvironmentVariable("ALLOWED_ORIGIN") ?? "localhost"}")
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        });
    });
}

// ============================================================================
// KESTREL CONFIGURATION
// ============================================================================

builder.WebHost.ConfigureKestrel(options =>
{
    // Set request body size limit
    options.Limits.MaxRequestBodySize = webhookSettings.MaxPayloadSizeBytes;

    // Set request header limits to prevent header bomb attacks
    options.Limits.MaxRequestHeaderCount = 100;
    options.Limits.MaxRequestHeadersTotalSize = 32 * 1024; // 32 KB
    options.Limits.MaxRequestLineSize = 8 * 1024; // 8 KB

    // Connection limits
    options.Limits.MaxConcurrentConnections = 1000;
    options.Limits.MaxConcurrentUpgradedConnections = 100;

    // Timeouts
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);

    // Disable detailed errors in production
    options.AddServerHeader = false;
});

// ============================================================================
// FORWARDED HEADERS (For Reverse Proxy Support)
// ============================================================================

if (forwardedHeadersConfig.Enabled)
{
    builder.Services.Configure<ForwardedHeadersOptions>(options =>
    {
        options.ForwardedHeaders = ForwardedHeaders.None;

        if (forwardedHeadersConfig.ForwardedForEnabled)
            options.ForwardedHeaders |= ForwardedHeaders.XForwardedFor;
        if (forwardedHeadersConfig.ForwardedProtoEnabled)
            options.ForwardedHeaders |= ForwardedHeaders.XForwardedProto;
        if (forwardedHeadersConfig.ForwardedHostEnabled)
            options.ForwardedHeaders |= ForwardedHeaders.XForwardedHost;

        options.ForwardLimit = forwardedHeadersConfig.ForwardLimit;

        // Clear default known networks (don't trust by default)
        options.KnownNetworks.Clear();
        options.KnownProxies.Clear();

        // Add trusted proxies
        foreach (var proxy in forwardedHeadersConfig.TrustedProxies)
        {
            if (IPAddress.TryParse(proxy, out var ip))
            {
                options.KnownProxies.Add(ip);
            }
        }

        // Add trusted networks
        foreach (var network in forwardedHeadersConfig.TrustedNetworks)
        {
            var parts = network.Split('/');
            if (parts.Length == 2 &&
                IPAddress.TryParse(parts[0], out var ip) &&
                int.TryParse(parts[1], out var prefixLength))
            {
                options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(ip, prefixLength));
            }
        }
    });
}

var app = builder.Build();

// ============================================================================
// STARTUP LOGGING
// ============================================================================

var logger = app.Services.GetRequiredService<ILogger<Program>>();
Program.StartTime = DateTime.UtcNow;

logger.LogInformation("VOLWebHook Service starting...");
logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);
logger.LogInformation("Max Payload Size: {Size} bytes", webhookSettings.MaxPayloadSizeBytes);
logger.LogInformation("Storage Path: {Path}", Path.GetFullPath(webhookSettings.PayloadStoragePath));

// ============================================================================
// MIDDLEWARE PIPELINE (Order is critical!)
// ============================================================================

// 1. Security headers (first - applies to all responses)
app.UseMiddleware<SecurityHeadersMiddleware>();

// 2. Forwarded headers (if behind reverse proxy)
if (forwardedHeadersConfig.Enabled)
{
    app.UseForwardedHeaders();
}

// 3. HTTPS redirection (force HTTPS in production)
if (app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

// 4. Static files for dashboard (served without authentication)
var wwwrootPath = Path.Combine(app.Environment.ContentRootPath, "wwwroot");
if (Directory.Exists(wwwrootPath))
{
    app.UseDefaultFiles();
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = ctx =>
        {
            // Add cache control headers for static assets
            if (ctx.Context.Request.Path.StartsWithSegments("/assets") ||
                ctx.Context.Request.Path.Value?.EndsWith(".js") == true ||
                ctx.Context.Request.Path.Value?.EndsWith(".css") == true)
            {
                ctx.Context.Response.Headers["Cache-Control"] = "public,max-age=3600";
            }
            else
            {
                ctx.Context.Response.Headers["Cache-Control"] = "no-cache,no-store,must-revalidate";
            }
        }
    });
}

// 5. CORS
if (app.Environment.IsDevelopment())
{
    app.UseCors("DevelopmentCors");
}
else
{
    app.UseCors("ProductionCors");
}

// 6. Routing
app.UseRouting();

// 7. Request validation for webhook endpoint
app.UseWhen(context => context.Request.Path.StartsWithSegments("/webhook"),
    appBuilder => appBuilder.UseMiddleware<RequestValidationMiddleware>());

// 8. Security middleware for webhook endpoint (in order of execution)
app.UseWhen(context => context.Request.Path.StartsWithSegments("/webhook"), appBuilder =>
{
    appBuilder.UseMiddleware<RequestBufferingMiddleware>();
    appBuilder.UseMiddleware<IpAllowlistMiddleware>();
    appBuilder.UseMiddleware<ApiKeyAuthenticationMiddleware>();
    appBuilder.UseMiddleware<HmacSignatureMiddleware>();
    appBuilder.UseMiddleware<RateLimitingMiddleware>();
});

// 9. Dashboard authentication for dashboard API endpoints
app.UseWhen(context => context.Request.Path.StartsWithSegments("/api/dashboard"),
    appBuilder => appBuilder.UseMiddleware<DashboardAuthenticationMiddleware>());

// 10. Controllers
app.MapControllers();

// 11. Health check (minimal response, no security details)
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var response = new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            service = "VOLWebHook",
            timestamp = DateTime.UtcNow,
            version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown"
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
});

// 12. SPA fallback (only if wwwroot exists)
if (Directory.Exists(wwwrootPath))
{
    app.MapFallbackToFile("index.html");
}

// ============================================================================
// STARTUP BANNER
// ============================================================================

Console.WriteLine();
Console.WriteLine("  ╔════════════════════════════════════════╗");
Console.WriteLine("  ║     VOLWebHook Service - READY         ║");
Console.WriteLine("  ╚════════════════════════════════════════╝");
Console.WriteLine();
Console.WriteLine($"  Environment:      {app.Environment.EnvironmentName}");
Console.WriteLine($"  Webhook Endpoint: POST /webhook");
Console.WriteLine($"  Health Check:     GET /health");
Console.WriteLine($"  Dashboard API:    /api/dashboard/*");
Console.WriteLine();
Console.WriteLine("  Storage:");
Console.WriteLine($"    Path:           {Path.GetFullPath(webhookSettings.PayloadStoragePath)}");
Console.WriteLine($"    Max Payload:    {webhookSettings.MaxPayloadSizeBytes / 1024 / 1024} MB");
Console.WriteLine($"    Retention:      {webhookSettings.PayloadRetentionDays} days");
Console.WriteLine();
Console.WriteLine("  Security Configuration:");
Console.WriteLine($"    IP Allowlist:   {(securitySettings.IpAllowlist.Enabled ? "✓ ENABLED" : "✗ disabled")}");
Console.WriteLine($"    API Key Auth:   {(securitySettings.ApiKey.Enabled ? "✓ ENABLED" : "✗ disabled")}");
Console.WriteLine($"    HMAC Signature: {(securitySettings.Hmac.Enabled ? "✓ ENABLED" : "✗ disabled")}");
Console.WriteLine($"    Rate Limiting:  {(securitySettings.RateLimit.Enabled ? "✓ ENABLED" : "✗ disabled")}");
Console.WriteLine($"    Dashboard Auth: {(dashboardSettings.RequireAuthentication ? "✓ ENABLED" : "✗ disabled")}");
Console.WriteLine();

if (!securitySettings.IpAllowlist.Enabled && !securitySettings.ApiKey.Enabled &&
    !securitySettings.Hmac.Enabled && app.Environment.IsProduction())
{
    logger.LogWarning("⚠️  WARNING: No webhook security features enabled in PRODUCTION! Enable IP allowlist, API key, or HMAC.");
}

if (!dashboardSettings.RequireAuthentication && app.Environment.IsProduction())
{
    logger.LogCritical("🔴 CRITICAL: Dashboard authentication is DISABLED in PRODUCTION! This is a severe security risk.");
}

Console.WriteLine("  ──────────────────────────────────────────");
Console.WriteLine();

logger.LogInformation("VOLWebHook service started successfully at {StartTime}", Program.StartTime);

// ============================================================================
// GRACEFUL SHUTDOWN
// ============================================================================

var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStopping.Register(() =>
{
    logger.LogInformation("VOLWebHook service is shutting down...");
});

lifetime.ApplicationStopped.Register(() =>
{
    logger.LogInformation("VOLWebHook service stopped at {StopTime}", DateTime.UtcNow);
    logger.LogInformation("Total uptime: {Uptime}", DateTime.UtcNow - Program.StartTime);
});

// ============================================================================
// RUN APPLICATION
// ============================================================================

await app.RunAsync();

// ============================================================================
// VALIDATION HELPER
// ============================================================================

static void ValidateConfiguration(
    WebhookSettings webhookSettings,
    SecuritySettings securitySettings,
    DashboardSettings dashboardSettings,
    IHostEnvironment environment)
{
    var errors = new List<string>();

    // Validate webhook settings
    if (webhookSettings.MaxPayloadSizeBytes <= 0)
        errors.Add("Webhook.MaxPayloadSizeBytes must be greater than 0");
    if (webhookSettings.MaxPayloadSizeBytes > 1024L * 1024 * 1024) // 1 GB
        errors.Add("Webhook.MaxPayloadSizeBytes exceeds maximum recommended size of 1 GB");
    if (string.IsNullOrWhiteSpace(webhookSettings.PayloadStoragePath))
        errors.Add("Webhook.PayloadStoragePath cannot be empty");
    if (webhookSettings.PayloadRetentionDays < 1)
        errors.Add("Webhook.PayloadRetentionDays must be at least 1");

    // Validate security settings
    if (securitySettings.IpAllowlist.Enabled && securitySettings.IpAllowlist.AllowedIps.Count == 0 &&
        securitySettings.IpAllowlist.AllowedCidrs.Count == 0 && !securitySettings.IpAllowlist.AllowPrivateNetworks)
    {
        errors.Add("Security.IpAllowlist is enabled but no IPs/CIDRs are configured and private networks are not allowed - this will block all requests");
    }

    if (securitySettings.ApiKey.Enabled && securitySettings.ApiKey.ValidKeys.Count == 0)
    {
        errors.Add("Security.ApiKey is enabled but no valid keys are configured - this will block all requests");
    }

    if (securitySettings.Hmac.Enabled && string.IsNullOrWhiteSpace(securitySettings.Hmac.SharedSecret))
    {
        errors.Add("Security.Hmac is enabled but SharedSecret is not configured - this will block all requests");
    }

    if (securitySettings.RateLimit.Enabled)
    {
        if (securitySettings.RateLimit.RequestsPerMinute <= 0)
            errors.Add("Security.RateLimit.RequestsPerMinute must be greater than 0");
        if (securitySettings.RateLimit.RequestsPerHour <= 0)
            errors.Add("Security.RateLimit.RequestsPerHour must be greater than 0");
        if (securitySettings.RateLimit.RequestsPerMinute > securitySettings.RateLimit.RequestsPerHour)
            errors.Add("Security.RateLimit.RequestsPerMinute cannot exceed RequestsPerHour");
    }

    // Validate dashboard settings
    if (dashboardSettings.RequireAuthentication && dashboardSettings.ValidApiKeys.Count == 0)
    {
        errors.Add("Dashboard.RequireAuthentication is enabled but no valid keys are configured - dashboard will be inaccessible");
    }

    if (environment.IsProduction())
    {
        // Production-specific validations
        if (!dashboardSettings.RequireAuthentication)
        {
            errors.Add("CRITICAL: Dashboard.RequireAuthentication MUST be enabled in Production environment");
        }

        if (!securitySettings.RateLimit.Enabled)
        {
            // Warning, not error
            Console.WriteLine("⚠️  WARNING: Rate limiting is disabled in Production - consider enabling it");
        }
    }

    if (errors.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("╔════════════════════════════════════════════════════╗");
        Console.WriteLine("║  CONFIGURATION VALIDATION ERRORS                   ║");
        Console.WriteLine("╚════════════════════════════════════════════════════╝");
        Console.WriteLine();
        foreach (var error in errors)
        {
            Console.WriteLine($"  ❌ {error}");
        }
        Console.WriteLine();
        throw new InvalidOperationException("Configuration validation failed. See errors above.");
    }
}

public partial class Program
{
    public static DateTime StartTime { get; set; }
}
