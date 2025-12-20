using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VOLWebHook.Api.Configuration;
using VOLWebHook.Api.Models;
using VOLWebHook.Api.Services;

namespace VOLWebHook.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly IWebhookPersistenceService _persistenceService;
    private readonly IOptionsMonitor<WebhookSettings> _webhookSettings;
    private readonly IOptionsMonitor<SecuritySettings> _securitySettings;
    private readonly IOptionsMonitor<LoggingSettings> _loggingSettings;
    private readonly IConfigurationWriterService _configWriter;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        IWebhookPersistenceService persistenceService,
        IOptionsMonitor<WebhookSettings> webhookSettings,
        IOptionsMonitor<SecuritySettings> securitySettings,
        IOptionsMonitor<LoggingSettings> loggingSettings,
        IConfigurationWriterService configWriter,
        IConfiguration configuration,
        ILogger<DashboardController> logger)
    {
        _persistenceService = persistenceService;
        _webhookSettings = webhookSettings;
        _securitySettings = securitySettings;
        _loggingSettings = loggingSettings;
        _configWriter = configWriter;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken cancellationToken)
    {
        var webhookSettings = _webhookSettings.CurrentValue;
        var securitySettings = _securitySettings.CurrentValue;

        var recentWebhooks = await _persistenceService.GetRecentAsync(100, cancellationToken);

        var now = DateTime.UtcNow;
        var todayWebhooks = recentWebhooks.Where(w => w.ReceivedAtUtc.Date == now.Date).ToList();
        var lastHourWebhooks = recentWebhooks.Where(w => w.ReceivedAtUtc >= now.AddHours(-1)).ToList();

        var validJsonCount = recentWebhooks.Count(w => w.IsValidJson);
        var totalSize = recentWebhooks.Sum(w => w.ContentLength);

        // Calculate storage statistics
        var storagePath = webhookSettings.PayloadStoragePath;
        long totalStorageBytes = 0;
        int totalFiles = 0;
        int totalDays = 0;

        if (Directory.Exists(storagePath))
        {
            var directories = Directory.GetDirectories(storagePath);
            totalDays = directories.Length;
            foreach (var dir in directories)
            {
                var files = Directory.GetFiles(dir, "*.json");
                totalFiles += files.Length;
                foreach (var file in files)
                {
                    try
                    {
                        totalStorageBytes += new FileInfo(file).Length;
                    }
                    catch { }
                }
            }
        }

        return Ok(new
        {
            uptime = DateTime.UtcNow - Program.StartTime,
            uptimeFormatted = FormatUptime(DateTime.UtcNow - Program.StartTime),
            startTime = Program.StartTime,

            webhooks = new
            {
                total = totalFiles,
                today = todayWebhooks.Count,
                lastHour = lastHourWebhooks.Count,
                validJsonPercent = recentWebhooks.Count > 0
                    ? Math.Round((double)validJsonCount / recentWebhooks.Count * 100, 1)
                    : 100,
                averageSize = recentWebhooks.Count > 0
                    ? totalSize / recentWebhooks.Count
                    : 0
            },

            storage = new
            {
                totalBytes = totalStorageBytes,
                totalFormatted = FormatBytes(totalStorageBytes),
                fileCount = totalFiles,
                daysStored = totalDays,
                path = storagePath,
                enabled = webhookSettings.EnablePayloadPersistence,
                retentionDays = webhookSettings.PayloadRetentionDays
            },

            security = new
            {
                ipAllowlist = securitySettings.IpAllowlist.Enabled,
                apiKey = securitySettings.ApiKey.Enabled,
                hmac = securitySettings.Hmac.Enabled,
                rateLimit = securitySettings.RateLimit.Enabled,
                activeFeatures = CountActiveSecurityFeatures(securitySettings)
            },

            limits = new
            {
                maxPayloadSizeBytes = webhookSettings.MaxPayloadSizeBytes,
                maxPayloadSizeFormatted = FormatBytes(webhookSettings.MaxPayloadSizeBytes),
                requestsPerMinute = securitySettings.RateLimit.RequestsPerMinute,
                requestsPerHour = securitySettings.RateLimit.RequestsPerHour
            }
        });
    }

    [HttpGet("webhooks")]
    public async Task<IActionResult> GetWebhooks(
        [FromQuery] int limit = 50,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var webhooks = await _persistenceService.GetRecentAsync(Math.Min(limit, 500), cancellationToken);

        if (!string.IsNullOrEmpty(search))
        {
            webhooks = webhooks
                .Where(w =>
                    w.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    w.SourceIpAddress.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    w.RawBody.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    w.Path.Contains(search, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var result = webhooks.Select(w => new
        {
            w.Id,
            w.ReceivedAtUtc,
            receivedAtFormatted = FormatRelativeTime(w.ReceivedAtUtc),
            w.HttpMethod,
            w.Path,
            w.QueryString,
            w.SourceIpAddress,
            w.SourcePort,
            w.ContentLength,
            contentLengthFormatted = FormatBytes(w.ContentLength),
            w.ContentType,
            w.IsValidJson,
            w.JsonParseError,
            headerCount = w.Headers.Count,
            bodyPreview = w.RawBody.Length > 200
                ? w.RawBody[..200] + "..."
                : w.RawBody
        });

        return Ok(new { webhooks = result, total = webhooks.Count });
    }

    [HttpGet("webhooks/{id}")]
    public async Task<IActionResult> GetWebhook(string id, CancellationToken cancellationToken)
    {
        var webhook = await _persistenceService.GetByIdAsync(id, cancellationToken);

        if (webhook == null)
            return NotFound(new { error = "Webhook not found", id });

        return Ok(new
        {
            webhook.Id,
            webhook.ReceivedAtUtc,
            receivedAtFormatted = FormatRelativeTime(webhook.ReceivedAtUtc),
            webhook.HttpMethod,
            webhook.Path,
            webhook.QueryString,
            webhook.SourceIpAddress,
            webhook.SourcePort,
            webhook.ContentLength,
            contentLengthFormatted = FormatBytes(webhook.ContentLength),
            webhook.ContentType,
            webhook.IsValidJson,
            webhook.JsonParseError,
            webhook.Headers,
            webhook.RawBody,
            formattedBody = webhook.IsValidJson ? FormatJson(webhook.RawBody) : null
        });
    }

    [HttpGet("config")]
    public IActionResult GetConfiguration()
    {
        var webhookSettings = _webhookSettings.CurrentValue;
        var securitySettings = _securitySettings.CurrentValue;
        var loggingSettings = _loggingSettings.CurrentValue;

        return Ok(new
        {
            webhook = new
            {
                maxPayloadSizeBytes = webhookSettings.MaxPayloadSizeBytes,
                maxPayloadSizeMB = webhookSettings.MaxPayloadSizeBytes / 1024 / 1024,
                alwaysReturn200 = webhookSettings.AlwaysReturn200,
                payloadStoragePath = webhookSettings.PayloadStoragePath,
                enablePayloadPersistence = webhookSettings.EnablePayloadPersistence,
                payloadRetentionDays = webhookSettings.PayloadRetentionDays
            },
            security = new
            {
                ipAllowlist = new
                {
                    enabled = securitySettings.IpAllowlist.Enabled,
                    allowedIps = securitySettings.IpAllowlist.AllowedIps,
                    allowedCidrs = securitySettings.IpAllowlist.AllowedCidrs,
                    allowPrivateNetworks = securitySettings.IpAllowlist.AllowPrivateNetworks
                },
                apiKey = new
                {
                    enabled = securitySettings.ApiKey.Enabled,
                    headerName = securitySettings.ApiKey.HeaderName,
                    keyCount = securitySettings.ApiKey.ValidKeys.Count
                },
                hmac = new
                {
                    enabled = securitySettings.Hmac.Enabled,
                    headerName = securitySettings.Hmac.HeaderName,
                    algorithm = securitySettings.Hmac.Algorithm,
                    hasSecret = !string.IsNullOrEmpty(securitySettings.Hmac.SharedSecret)
                },
                rateLimit = new
                {
                    enabled = securitySettings.RateLimit.Enabled,
                    requestsPerMinute = securitySettings.RateLimit.RequestsPerMinute,
                    requestsPerHour = securitySettings.RateLimit.RequestsPerHour,
                    perIpAddress = securitySettings.RateLimit.PerIpAddress
                }
            },
            logging = new
            {
                enabled = loggingSettings.Enabled,
                logDirectory = loggingSettings.LogDirectory,
                fileNamePattern = loggingSettings.FileNamePattern,
                maxFileSizeBytes = loggingSettings.MaxFileSizeBytes,
                maxFileSizeMB = loggingSettings.MaxFileSizeBytes / 1024 / 1024
            }
        });
    }

    [HttpPost("config")]
    public async Task<IActionResult> SaveConfiguration(
        [FromBody] ConfigurationUpdateRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            await _configWriter.SaveConfigurationAsync(request, cancellationToken);

            _logger.LogInformation("Configuration updated via dashboard");

            // Return the updated configuration
            // Give the file watcher a moment to detect the change
            await Task.Delay(100, cancellationToken);

            return Ok(new
            {
                success = true,
                message = "Configuration updated successfully. Changes are now active."
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save configuration");
            return StatusCode(500, new
            {
                success = false,
                message = "Failed to save configuration",
                error = ex.Message
            });
        }
    }

    [HttpGet("endpoint-info")]
    public IActionResult GetEndpointInfo()
    {
        var request = HttpContext.Request;
        var baseUrl = $"{request.Scheme}://{request.Host}";

        return Ok(new
        {
            webhookUrl = $"{baseUrl}/webhook",
            healthUrl = $"{baseUrl}/health",
            dashboardUrl = baseUrl,

            exampleCurl = $"curl -X POST {baseUrl}/webhook -H \"Content-Type: application/json\" -d '{{\"event\": \"test\", \"data\": {{\"message\": \"Hello World\"}}}}'",

            supportedMethods = new[] { "POST" },
            supportedContentTypes = new[] { "application/json", "text/plain", "application/x-www-form-urlencoded" },

            responseHeaders = new
            {
                xRequestId = "Unique ID for tracking this webhook request"
            },

            documentation = new
            {
                description = "VOLWebHook is a production-quality webhook capture service. Send POST requests to /webhook to capture and store webhook payloads.",
                features = new[]
                {
                    "Captures and stores webhook payloads with full metadata",
                    "Supports IP allowlisting for access control",
                    "API key authentication for secure access",
                    "HMAC signature verification for payload integrity",
                    "Rate limiting to prevent abuse",
                    "Automatic JSON validation and formatting",
                    "Rolling file storage with configurable retention"
                }
            }
        });
    }

    [HttpPost("test-webhook")]
    public async Task<IActionResult> SendTestWebhook(CancellationToken cancellationToken)
    {
        var testPayload = new
        {
            @event = "test.webhook",
            timestamp = DateTime.UtcNow,
            source = "dashboard",
            data = new
            {
                message = "This is a test webhook sent from the dashboard",
                randomId = Guid.NewGuid().ToString("N")[..8]
            }
        };

        // Create a simulated webhook request
        var webhookRequest = new WebhookRequest
        {
            Id = Guid.NewGuid().ToString("N"),
            ReceivedAtUtc = DateTime.UtcNow,
            HttpMethod = "POST",
            Path = "/webhook",
            SourceIpAddress = "127.0.0.1",
            SourcePort = 0,
            Headers = new Dictionary<string, string[]>
            {
                ["Content-Type"] = new[] { "application/json" },
                ["X-Test-Source"] = new[] { "Dashboard" }
            },
            RawBody = JsonSerializer.Serialize(testPayload),
            ContentLength = JsonSerializer.Serialize(testPayload).Length,
            ContentType = "application/json",
            IsValidJson = true
        };

        await _persistenceService.SaveAsync(webhookRequest, cancellationToken);

        return Ok(new
        {
            success = true,
            message = "Test webhook sent successfully",
            requestId = webhookRequest.Id,
            payload = testPayload
        });
    }

    [HttpPost("cleanup")]
    public async Task<IActionResult> CleanupOldWebhooks(
        [FromQuery] int? retentionDays = null,
        CancellationToken cancellationToken = default)
    {
        var days = retentionDays ?? _webhookSettings.CurrentValue.PayloadRetentionDays;
        var deletedCount = await _persistenceService.CleanupOldEntriesAsync(days, cancellationToken);

        return Ok(new
        {
            success = true,
            deletedCount,
            retentionDays = days,
            message = $"Cleaned up {deletedCount} webhook(s) older than {days} days"
        });
    }

    [HttpPost("generate-api-key")]
    public async Task<IActionResult> GenerateApiKey(CancellationToken cancellationToken)
    {
        try
        {
            // Generate a cryptographically secure API key
            var keyBytes = RandomNumberGenerator.GetBytes(32);
            var apiKey = $"vwh_{Convert.ToBase64String(keyBytes).Replace("+", "").Replace("/", "").Replace("=", "")[..40]}";

            // Get existing keys and add the new one
            var currentSettings = _securitySettings.CurrentValue;
            var existingKeys = currentSettings.ApiKey.ValidKeys.ToList();
            existingKeys.Add(apiKey);

            // Save the updated configuration
            var updateRequest = new ConfigurationUpdateRequest
            {
                Security = new SecuritySettingsUpdate
                {
                    ApiKey = new ApiKeySettingsUpdate
                    {
                        ValidKeys = existingKeys
                    }
                }
            };

            await _configWriter.SaveConfigurationAsync(updateRequest, cancellationToken);

            _logger.LogInformation("New API key generated and added to allowed list");

            // Wait for config reload
            await Task.Delay(100, cancellationToken);

            return Ok(new
            {
                success = true,
                apiKey,
                message = "API key generated and added to allowed list. Copy this key now - it won't be shown again.",
                totalKeys = existingKeys.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate API key");
            return StatusCode(500, new
            {
                success = false,
                message = "Failed to generate API key",
                error = ex.Message
            });
        }
    }

    private static int CountActiveSecurityFeatures(SecuritySettings settings)
    {
        var count = 0;
        if (settings.IpAllowlist.Enabled) count++;
        if (settings.ApiKey.Enabled) count++;
        if (settings.Hmac.Enabled) count++;
        if (settings.RateLimit.Enabled) count++;
        return count;
    }

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        if (uptime.TotalHours >= 1)
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s";
        if (uptime.TotalMinutes >= 1)
            return $"{(int)uptime.TotalMinutes}m {uptime.Seconds}s";
        return $"{uptime.Seconds}s";
    }

    private static string FormatRelativeTime(DateTime utcTime)
    {
        var diff = DateTime.UtcNow - utcTime;

        if (diff.TotalSeconds < 60)
            return "just now";
        if (diff.TotalMinutes < 60)
            return $"{(int)diff.TotalMinutes}m ago";
        if (diff.TotalHours < 24)
            return $"{(int)diff.TotalHours}h ago";
        if (diff.TotalDays < 7)
            return $"{(int)diff.TotalDays}d ago";

        return utcTime.ToString("MMM d, yyyy");
    }

    private static string? FormatJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return null;
        }
    }
}
