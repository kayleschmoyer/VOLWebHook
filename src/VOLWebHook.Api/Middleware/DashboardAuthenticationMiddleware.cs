using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using VOLWebHook.Api.Configuration;

namespace VOLWebHook.Api.Middleware;

/// <summary>
/// Authenticates dashboard API requests using API key or configured credentials.
/// CRITICAL: Dashboard must be protected as it provides full access to webhooks and configuration.
/// </summary>
public sealed class DashboardAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<DashboardAuthenticationMiddleware> _logger;
    private readonly IOptionsMonitor<DashboardSettings> _dashboardSettings;
    private volatile CachedDashboardSettings? _cachedSettings;
    private readonly object _cacheLock = new();

    public DashboardAuthenticationMiddleware(
        RequestDelegate next,
        IOptionsMonitor<DashboardSettings> settings,
        ILogger<DashboardAuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _dashboardSettings = settings;

        UpdateCache(settings.CurrentValue);
        settings.OnChange(UpdateCache);
    }

    private void UpdateCache(DashboardSettings settings)
    {
        lock (_cacheLock)
        {
            var validKeyHashes = new HashSet<string>(
                settings.ValidApiKeys.Select(ComputeHash),
                StringComparer.Ordinal);

            _cachedSettings = new CachedDashboardSettings(settings, validKeyHashes);

            _logger.LogInformation(
                "Dashboard authentication configuration reloaded: Enabled={Enabled}, KeyCount={KeyCount}",
                settings.RequireAuthentication, settings.ValidApiKeys.Count);
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var cached = _cachedSettings;
        if (cached == null || !cached.Settings.RequireAuthentication)
        {
            await _next(context);
            return;
        }

        var providedKey = context.Request.Headers[cached.Settings.HeaderName].FirstOrDefault();

        if (string.IsNullOrEmpty(providedKey))
        {
            _logger.LogWarning("Unauthorized dashboard access attempt from {IpAddress} - missing API key",
                context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers["WWW-Authenticate"] = $"ApiKey realm=\"Dashboard\"";
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized", message = "Dashboard API key required" });
            return;
        }

        var providedKeyHash = ComputeHash(providedKey);
        if (!cached.ValidKeyHashes.Contains(providedKeyHash))
        {
            _logger.LogWarning("Unauthorized dashboard access attempt from {IpAddress} - invalid API key",
                context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Unauthorized", message = "Invalid dashboard API key" });
            return;
        }

        await _next(context);
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    private sealed class CachedDashboardSettings
    {
        public DashboardSettings Settings { get; }
        public HashSet<string> ValidKeyHashes { get; }

        public CachedDashboardSettings(DashboardSettings settings, HashSet<string> validKeyHashes)
        {
            Settings = settings;
            ValidKeyHashes = validKeyHashes;
        }
    }
}
