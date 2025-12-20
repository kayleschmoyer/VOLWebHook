using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using VOLWebHook.Api.Configuration;

namespace VOLWebHook.Api.Middleware;

public sealed class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly IOptionsMonitor<SecuritySettings> _securitySettings;
    private readonly ConcurrentDictionary<string, RateLimitCounter> _counters = new();
    private readonly Timer _cleanupTimer;

    public RateLimitingMiddleware(
        RequestDelegate next,
        IOptionsMonitor<SecuritySettings> settings,
        ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _securitySettings = settings;

        // Cleanup expired entries every 5 minutes
        _cleanupTimer = new Timer(CleanupExpiredEntries, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));

        // Log configuration changes
        settings.OnChange(newSettings =>
        {
            var rateLimit = newSettings.RateLimit;
            _logger.LogInformation(
                "Rate limit configuration reloaded: Enabled={Enabled}, PerMin={PerMin}, PerHour={PerHour}",
                rateLimit.Enabled, rateLimit.RequestsPerMinute, rateLimit.RequestsPerHour);
        });
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var settings = _securitySettings.CurrentValue.RateLimit;

        if (!settings.Enabled)
        {
            await _next(context);
            return;
        }

        var clientKey = GetClientKey(context, settings);
        var counter = _counters.GetOrAdd(clientKey, _ => new RateLimitCounter());

        var now = DateTime.UtcNow;
        counter.CleanupOldRequests(now);
        counter.AddRequest(now);

        var minuteCount = counter.GetMinuteCount(now);
        var hourCount = counter.GetHourCount(now);

        if (minuteCount >= settings.RequestsPerMinute)
        {
            _logger.LogWarning("Rate limit exceeded (per minute) for client {ClientKey}: {Count}/{Limit}",
                clientKey, minuteCount, settings.RequestsPerMinute);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.Append("Retry-After", "60");
            await context.Response.WriteAsync("Rate limit exceeded. Try again later.");
            return;
        }

        if (hourCount >= settings.RequestsPerHour)
        {
            _logger.LogWarning("Rate limit exceeded (per hour) for client {ClientKey}: {Count}/{Limit}",
                clientKey, hourCount, settings.RequestsPerHour);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.Append("Retry-After", "3600");
            await context.Response.WriteAsync("Rate limit exceeded. Try again later.");
            return;
        }

        await _next(context);
    }

    private static string GetClientKey(HttpContext context, RateLimitSettings settings)
    {
        if (!settings.PerIpAddress)
        {
            return "global";
        }

        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',').FirstOrDefault()?.Trim() ?? "unknown";
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private void CleanupExpiredEntries(object? state)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddHours(-2);
        var keysToRemove = new List<string>();

        foreach (var kvp in _counters)
        {
            if (kvp.Value.LastRequest < cutoff)
            {
                keysToRemove.Add(kvp.Key);
            }
        }

        foreach (var key in keysToRemove)
        {
            _counters.TryRemove(key, out _);
        }
    }

    private sealed class RateLimitCounter
    {
        private readonly object _lock = new();
        private readonly Queue<DateTime> _requests = new();

        public DateTime LastRequest { get; private set; } = DateTime.MinValue;

        public void AddRequest(DateTime timestamp)
        {
            lock (_lock)
            {
                _requests.Enqueue(timestamp);
                LastRequest = timestamp;
            }
        }

        public void CleanupOldRequests(DateTime now)
        {
            lock (_lock)
            {
                var cutoff = now.AddHours(-1);
                while (_requests.Count > 0 && _requests.Peek() < cutoff)
                {
                    _requests.Dequeue();
                }
            }
        }

        public int GetMinuteCount(DateTime now)
        {
            lock (_lock)
            {
                var cutoff = now.AddMinutes(-1);
                return _requests.Count(r => r >= cutoff);
            }
        }

        public int GetHourCount(DateTime now)
        {
            lock (_lock)
            {
                var cutoff = now.AddHours(-1);
                return _requests.Count(r => r >= cutoff);
            }
        }
    }
}
