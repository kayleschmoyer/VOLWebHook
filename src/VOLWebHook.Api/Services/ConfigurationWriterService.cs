using System.Text.Json;
using System.Text.Json.Nodes;
using VOLWebHook.Api.Configuration;

namespace VOLWebHook.Api.Services;

public interface IConfigurationWriterService
{
    Task SaveConfigurationAsync(ConfigurationUpdateRequest request, CancellationToken cancellationToken = default);
}

public class ConfigurationUpdateRequest
{
    public WebhookSettingsUpdate? Webhook { get; set; }
    public SecuritySettingsUpdate? Security { get; set; }
    public LoggingSettingsUpdate? Logging { get; set; }
}

public class WebhookSettingsUpdate
{
    public long? MaxPayloadSizeBytes { get; set; }
    public bool? AlwaysReturn200 { get; set; }
    public string? PayloadStoragePath { get; set; }
    public bool? EnablePayloadPersistence { get; set; }
    public int? PayloadRetentionDays { get; set; }
}

public class SecuritySettingsUpdate
{
    public IpAllowlistSettingsUpdate? IpAllowlist { get; set; }
    public ApiKeySettingsUpdate? ApiKey { get; set; }
    public HmacSettingsUpdate? Hmac { get; set; }
    public RateLimitSettingsUpdate? RateLimit { get; set; }
}

public class IpAllowlistSettingsUpdate
{
    public bool? Enabled { get; set; }
    public List<string>? AllowedIps { get; set; }
    public List<string>? AllowedCidrs { get; set; }
    public bool? AllowPrivateNetworks { get; set; }
}

public class ApiKeySettingsUpdate
{
    public bool? Enabled { get; set; }
    public string? HeaderName { get; set; }
    public List<string>? ValidKeys { get; set; }
}

public class HmacSettingsUpdate
{
    public bool? Enabled { get; set; }
    public string? HeaderName { get; set; }
    public string? Algorithm { get; set; }
    public string? SharedSecret { get; set; }
}

public class RateLimitSettingsUpdate
{
    public bool? Enabled { get; set; }
    public int? RequestsPerMinute { get; set; }
    public int? RequestsPerHour { get; set; }
    public bool? PerIpAddress { get; set; }
}

public class LoggingSettingsUpdate
{
    public bool? Enabled { get; set; }
    public string? LogDirectory { get; set; }
    public string? FileNamePattern { get; set; }
    public long? MaxFileSizeBytes { get; set; }
}

public class ConfigurationWriterService : IConfigurationWriterService
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ConfigurationWriterService> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null // Keep original casing
    };

    public ConfigurationWriterService(
        IWebHostEnvironment environment,
        ILogger<ConfigurationWriterService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async Task SaveConfigurationAsync(ConfigurationUpdateRequest request, CancellationToken cancellationToken = default)
    {
        var configPath = Path.Combine(_environment.ContentRootPath, "appsettings.json");

        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            // Read existing configuration
            var json = await File.ReadAllTextAsync(configPath, cancellationToken);
            var config = JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidOperationException("Invalid appsettings.json format");

            // Apply updates
            if (request.Webhook != null)
            {
                ApplyWebhookUpdates(config, request.Webhook);
            }

            if (request.Security != null)
            {
                ApplySecurityUpdates(config, request.Security);
            }

            if (request.Logging != null)
            {
                ApplyLoggingUpdates(config, request.Logging);
            }

            // Write back to file
            var updatedJson = config.ToJsonString(JsonOptions);
            await File.WriteAllTextAsync(configPath, updatedJson, cancellationToken);

            _logger.LogInformation("Configuration updated successfully");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static void ApplyWebhookUpdates(JsonObject config, WebhookSettingsUpdate updates)
    {
        var webhook = config[WebhookSettings.SectionName] as JsonObject ?? new JsonObject();

        if (updates.MaxPayloadSizeBytes.HasValue)
            webhook["MaxPayloadSizeBytes"] = updates.MaxPayloadSizeBytes.Value;
        if (updates.AlwaysReturn200.HasValue)
            webhook["AlwaysReturn200"] = updates.AlwaysReturn200.Value;
        if (updates.PayloadStoragePath != null)
            webhook["PayloadStoragePath"] = updates.PayloadStoragePath;
        if (updates.EnablePayloadPersistence.HasValue)
            webhook["EnablePayloadPersistence"] = updates.EnablePayloadPersistence.Value;
        if (updates.PayloadRetentionDays.HasValue)
            webhook["PayloadRetentionDays"] = updates.PayloadRetentionDays.Value;

        config[WebhookSettings.SectionName] = webhook;
    }

    private static void ApplySecurityUpdates(JsonObject config, SecuritySettingsUpdate updates)
    {
        var security = config[SecuritySettings.SectionName] as JsonObject ?? new JsonObject();

        if (updates.IpAllowlist != null)
        {
            var ipAllowlist = security["IpAllowlist"] as JsonObject ?? new JsonObject();

            if (updates.IpAllowlist.Enabled.HasValue)
                ipAllowlist["Enabled"] = updates.IpAllowlist.Enabled.Value;
            if (updates.IpAllowlist.AllowedIps != null)
                ipAllowlist["AllowedIps"] = new JsonArray(updates.IpAllowlist.AllowedIps.Select(ip => JsonValue.Create(ip)).ToArray());
            if (updates.IpAllowlist.AllowedCidrs != null)
                ipAllowlist["AllowedCidrs"] = new JsonArray(updates.IpAllowlist.AllowedCidrs.Select(cidr => JsonValue.Create(cidr)).ToArray());
            if (updates.IpAllowlist.AllowPrivateNetworks.HasValue)
                ipAllowlist["AllowPrivateNetworks"] = updates.IpAllowlist.AllowPrivateNetworks.Value;

            security["IpAllowlist"] = ipAllowlist;
        }

        if (updates.ApiKey != null)
        {
            var apiKey = security["ApiKey"] as JsonObject ?? new JsonObject();

            if (updates.ApiKey.Enabled.HasValue)
                apiKey["Enabled"] = updates.ApiKey.Enabled.Value;
            if (updates.ApiKey.HeaderName != null)
                apiKey["HeaderName"] = updates.ApiKey.HeaderName;
            if (updates.ApiKey.ValidKeys != null)
                apiKey["ValidKeys"] = new JsonArray(updates.ApiKey.ValidKeys.Select(k => JsonValue.Create(k)).ToArray());

            security["ApiKey"] = apiKey;
        }

        if (updates.Hmac != null)
        {
            var hmac = security["Hmac"] as JsonObject ?? new JsonObject();

            if (updates.Hmac.Enabled.HasValue)
                hmac["Enabled"] = updates.Hmac.Enabled.Value;
            if (updates.Hmac.HeaderName != null)
                hmac["HeaderName"] = updates.Hmac.HeaderName;
            if (updates.Hmac.Algorithm != null)
                hmac["Algorithm"] = updates.Hmac.Algorithm;
            if (updates.Hmac.SharedSecret != null)
                hmac["SharedSecret"] = updates.Hmac.SharedSecret;

            security["Hmac"] = hmac;
        }

        if (updates.RateLimit != null)
        {
            var rateLimit = security["RateLimit"] as JsonObject ?? new JsonObject();

            if (updates.RateLimit.Enabled.HasValue)
                rateLimit["Enabled"] = updates.RateLimit.Enabled.Value;
            if (updates.RateLimit.RequestsPerMinute.HasValue)
                rateLimit["RequestsPerMinute"] = updates.RateLimit.RequestsPerMinute.Value;
            if (updates.RateLimit.RequestsPerHour.HasValue)
                rateLimit["RequestsPerHour"] = updates.RateLimit.RequestsPerHour.Value;
            if (updates.RateLimit.PerIpAddress.HasValue)
                rateLimit["PerIpAddress"] = updates.RateLimit.PerIpAddress.Value;

            security["RateLimit"] = rateLimit;
        }

        config[SecuritySettings.SectionName] = security;
    }

    private static void ApplyLoggingUpdates(JsonObject config, LoggingSettingsUpdate updates)
    {
        var logging = config[LoggingSettings.SectionName] as JsonObject ?? new JsonObject();

        if (updates.Enabled.HasValue)
            logging["Enabled"] = updates.Enabled.Value;
        if (updates.LogDirectory != null)
            logging["LogDirectory"] = updates.LogDirectory;
        if (updates.FileNamePattern != null)
            logging["FileNamePattern"] = updates.FileNamePattern;
        if (updates.MaxFileSizeBytes.HasValue)
            logging["MaxFileSizeBytes"] = updates.MaxFileSizeBytes.Value;

        config[LoggingSettings.SectionName] = logging;
    }
}
