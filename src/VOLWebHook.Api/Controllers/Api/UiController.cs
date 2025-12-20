using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VOLWebHook.Api.Configuration;
using VOLWebHook.Api.Services;

namespace VOLWebHook.Api.Controllers.Api;

/// <summary>
/// API controller providing endpoints for the VOLWebHook UI dashboard.
/// </summary>
[ApiController]
[Route("api/ui")]
public class UiController : ControllerBase
{
    private readonly IWebhookPersistenceService _persistenceService;
    private readonly IOptionsMonitor<WebhookSettings> _webhookSettings;
    private readonly IOptionsMonitor<SecuritySettings> _securitySettings;
    private readonly ILogger<UiController> _logger;

    public UiController(
        IWebhookPersistenceService persistenceService,
        IOptionsMonitor<WebhookSettings> webhookSettings,
        IOptionsMonitor<SecuritySettings> securitySettings,
        ILogger<UiController> logger)
    {
        _persistenceService = persistenceService;
        _webhookSettings = webhookSettings;
        _securitySettings = securitySettings;
        _logger = logger;
    }

    /// <summary>
    /// Get a list of recent webhooks.
    /// </summary>
    [HttpGet("webhooks")]
    public async Task<IActionResult> GetWebhooks([FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        try
        {
            var webhooks = await _persistenceService.GetRecentAsync(Math.Min(limit, 500), cancellationToken);

            var result = webhooks.Select(w => new
            {
                w.Id,
                w.ReceivedAtUtc,
                w.HttpMethod,
                w.Path,
                w.SourceIpAddress,
                w.ContentLength,
                w.ContentType,
                w.IsValidJson
            });

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve webhooks");
            return Ok(Array.Empty<object>());
        }
    }

    /// <summary>
    /// Get details of a specific webhook.
    /// </summary>
    [HttpGet("webhooks/{id}")]
    public async Task<IActionResult> GetWebhook(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var webhook = await _persistenceService.GetByIdAsync(id, cancellationToken);

            if (webhook == null)
            {
                return NotFound(new { message = "Webhook not found" });
            }

            return Ok(webhook);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve webhook {Id}", id);
            return NotFound(new { message = "Webhook not found" });
        }
    }

    /// <summary>
    /// Get webhook statistics.
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken cancellationToken = default)
    {
        try
        {
            var allWebhooks = await _persistenceService.GetRecentAsync(10000, cancellationToken);
            var today = DateTime.UtcNow.Date;

            var totalWebhooks = allWebhooks.Count;
            var todayWebhooks = allWebhooks.Count(w => w.ReceivedAtUtc.Date == today);
            var validJsonCount = allWebhooks.Count(w => w.IsValidJson);
            var successRate = totalWebhooks > 0 ? (validJsonCount * 100 / totalWebhooks) : 100;
            var avgPayloadSize = totalWebhooks > 0 ? (long)allWebhooks.Average(w => w.ContentLength) : 0;

            return Ok(new
            {
                totalWebhooks,
                todayWebhooks,
                successRate,
                avgPayloadSize
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate stats");
            return Ok(new
            {
                totalWebhooks = 0,
                todayWebhooks = 0,
                successRate = 100,
                avgPayloadSize = 0
            });
        }
    }

    /// <summary>
    /// Get current configuration (read-only view for UI).
    /// </summary>
    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        var webhookSettings = _webhookSettings.CurrentValue;
        var securitySettings = _securitySettings.CurrentValue;

        return Ok(new
        {
            webhook = new
            {
                maxPayloadSizeMB = webhookSettings.MaxPayloadSizeBytes / 1024 / 1024,
                payloadStoragePath = webhookSettings.PayloadStoragePath,
                enablePayloadPersistence = webhookSettings.EnablePayloadPersistence,
                payloadRetentionDays = webhookSettings.PayloadRetentionDays,
                alwaysReturn200 = webhookSettings.AlwaysReturn200
            },
            security = new
            {
                ipAllowlist = new
                {
                    enabled = securitySettings.IpAllowlist.Enabled,
                    allowPrivateNetworks = securitySettings.IpAllowlist.AllowPrivateNetworks
                },
                apiKey = new
                {
                    enabled = securitySettings.ApiKey.Enabled,
                    headerName = securitySettings.ApiKey.HeaderName
                },
                hmac = new
                {
                    enabled = securitySettings.Hmac.Enabled,
                    headerName = securitySettings.Hmac.HeaderName,
                    algorithm = securitySettings.Hmac.Algorithm
                },
                rateLimit = new
                {
                    enabled = securitySettings.RateLimit.Enabled,
                    requestsPerMinute = securitySettings.RateLimit.RequestsPerMinute,
                    requestsPerHour = securitySettings.RateLimit.RequestsPerHour,
                    perIpAddress = securitySettings.RateLimit.PerIpAddress
                }
            }
        });
    }

    /// <summary>
    /// Update configuration (placeholder - in production, this would update appsettings).
    /// </summary>
    [HttpPost("config")]
    public IActionResult SaveConfig([FromBody] JsonElement config)
    {
        // In a production system, this would:
        // 1. Validate the configuration
        // 2. Write to appsettings.json or a database
        // 3. Trigger a configuration reload

        _logger.LogInformation("Configuration update requested (read-only in this version)");

        return Ok(new
        {
            message = "Configuration received. Note: In this version, configuration changes must be made in appsettings.json and the service restarted.",
            received = config
        });
    }

    /// <summary>
    /// Get recent log entries (placeholder - reads from log files if available).
    /// </summary>
    [HttpGet("logs")]
    public IActionResult GetLogs([FromQuery] int limit = 100)
    {
        // In a production system, this would read from log files or a log aggregation service
        // For now, return an empty array as logs are stored in files

        return Ok(Array.Empty<object>());
    }

    /// <summary>
    /// Delete a specific webhook.
    /// </summary>
    [HttpDelete("webhooks/{id}")]
    public async Task<IActionResult> DeleteWebhook(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var webhook = await _persistenceService.GetByIdAsync(id, cancellationToken);

            if (webhook == null)
            {
                return NotFound(new { message = "Webhook not found" });
            }

            // Note: The current IWebhookPersistenceService doesn't have a Delete method
            // In a production system, you would add this method to the interface

            return Ok(new { message = "Delete functionality not implemented in current version" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete webhook {Id}", id);
            return StatusCode(500, new { message = "Failed to delete webhook" });
        }
    }

    /// <summary>
    /// Trigger cleanup of old webhooks.
    /// </summary>
    [HttpPost("cleanup")]
    public async Task<IActionResult> TriggerCleanup(CancellationToken cancellationToken = default)
    {
        try
        {
            var retentionDays = _webhookSettings.CurrentValue.PayloadRetentionDays;
            var deletedCount = await _persistenceService.CleanupOldEntriesAsync(retentionDays, cancellationToken);

            return Ok(new
            {
                message = $"Cleanup completed. Removed {deletedCount} old entries.",
                deletedCount,
                retentionDays
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run cleanup");
            return StatusCode(500, new { message = "Cleanup failed" });
        }
    }

    /// <summary>
    /// Export webhooks as JSON.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> ExportWebhooks([FromQuery] int limit = 1000, CancellationToken cancellationToken = default)
    {
        try
        {
            var webhooks = await _persistenceService.GetRecentAsync(Math.Min(limit, 10000), cancellationToken);

            var json = JsonSerializer.Serialize(webhooks, new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            return File(
                System.Text.Encoding.UTF8.GetBytes(json),
                "application/json",
                $"webhooks-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export webhooks");
            return StatusCode(500, new { message = "Export failed" });
        }
    }
}
