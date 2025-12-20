using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using VOLWebHook.Api.Configuration;

namespace VOLWebHook.Api.Middleware;

public sealed class ApiKeyAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;
    private readonly IOptionsMonitor<SecuritySettings> _securitySettings;

    // Cache for hashed keys, updated when configuration changes
    private volatile CachedApiKeySettings? _cachedSettings;
    private readonly object _cacheLock = new();

    public ApiKeyAuthenticationMiddleware(
        RequestDelegate next,
        IOptionsMonitor<SecuritySettings> settings,
        ILogger<ApiKeyAuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _securitySettings = settings;

        // Initialize cache
        UpdateCache(settings.CurrentValue.ApiKey);

        // Subscribe to configuration changes
        settings.OnChange(newSettings => UpdateCache(newSettings.ApiKey));
    }

    private void UpdateCache(ApiKeySettings settings)
    {
        lock (_cacheLock)
        {
            var validKeyHashes = new HashSet<string>(
                settings.ValidKeys.Select(ComputeHash),
                StringComparer.Ordinal);

            _cachedSettings = new CachedApiKeySettings(settings, validKeyHashes);

            _logger.LogInformation(
                "API key configuration reloaded: Enabled={Enabled}, KeyCount={KeyCount}",
                settings.Enabled, settings.ValidKeys.Count);
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var cached = _cachedSettings;
        if (cached == null || !cached.Settings.Enabled)
        {
            await _next(context);
            return;
        }

        var providedKey = context.Request.Headers[cached.Settings.HeaderName].FirstOrDefault();

        if (string.IsNullOrEmpty(providedKey))
        {
            _logger.LogWarning("Missing API key in request from {IpAddress}",
                context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized: API key required");
            return;
        }

        var providedKeyHash = ComputeHash(providedKey);
        if (!cached.ValidKeyHashes.Contains(providedKeyHash))
        {
            _logger.LogWarning("Invalid API key in request from {IpAddress}",
                context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized: Invalid API key");
            return;
        }

        await _next(context);
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    private sealed class CachedApiKeySettings
    {
        public ApiKeySettings Settings { get; }
        public HashSet<string> ValidKeyHashes { get; }

        public CachedApiKeySettings(ApiKeySettings settings, HashSet<string> validKeyHashes)
        {
            Settings = settings;
            ValidKeyHashes = validKeyHashes;
        }
    }
}
