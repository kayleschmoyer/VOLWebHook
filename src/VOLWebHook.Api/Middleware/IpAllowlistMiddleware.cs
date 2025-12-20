using System.Net;
using Microsoft.Extensions.Options;
using VOLWebHook.Api.Configuration;

namespace VOLWebHook.Api.Middleware;

public sealed class IpAllowlistMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<IpAllowlistMiddleware> _logger;
    private readonly IOptionsMonitor<SecuritySettings> _securitySettings;

    // Cache for parsed settings, updated when configuration changes
    private volatile CachedIpAllowlistSettings? _cachedSettings;
    private readonly object _cacheLock = new();

    public IpAllowlistMiddleware(
        RequestDelegate next,
        IOptionsMonitor<SecuritySettings> settings,
        ILogger<IpAllowlistMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _securitySettings = settings;

        // Initialize cache
        UpdateCache(settings.CurrentValue.IpAllowlist);

        // Subscribe to configuration changes
        settings.OnChange(newSettings => UpdateCache(newSettings.IpAllowlist));
    }

    private void UpdateCache(IpAllowlistSettings settings)
    {
        lock (_cacheLock)
        {
            _cachedSettings = new CachedIpAllowlistSettings(
                settings,
                new HashSet<string>(settings.AllowedIps, StringComparer.OrdinalIgnoreCase),
                ParseCidrs(settings.AllowedCidrs));

            _logger.LogInformation(
                "IP allowlist configuration reloaded: Enabled={Enabled}, IPs={IpCount}, CIDRs={CidrCount}",
                settings.Enabled, settings.AllowedIps.Count, settings.AllowedCidrs.Count);
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

        var remoteIp = GetRemoteIpAddress(context);
        if (remoteIp == null)
        {
            _logger.LogWarning("Unable to determine remote IP address");
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("Forbidden");
            return;
        }

        if (IsIpAllowed(remoteIp, cached))
        {
            await _next(context);
            return;
        }

        _logger.LogWarning("Request from blocked IP address: {IpAddress}", remoteIp);
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsync("Forbidden");
    }

    private static bool IsIpAllowed(IPAddress ip, CachedIpAllowlistSettings cached)
    {
        // Check explicit IP list
        if (cached.AllowedIps.Contains(ip.ToString()))
        {
            return true;
        }

        // Check CIDR ranges
        foreach (var (network, prefixLength) in cached.AllowedCidrs)
        {
            if (IsInRange(ip, network, prefixLength))
            {
                return true;
            }
        }

        // Check private networks if allowed
        if (cached.Settings.AllowPrivateNetworks && IsPrivateNetwork(ip))
        {
            return true;
        }

        return false;
    }

    private static bool IsInRange(IPAddress ip, IPAddress network, int prefixLength)
    {
        var ipBytes = ip.GetAddressBytes();
        var networkBytes = network.GetAddressBytes();

        if (ipBytes.Length != networkBytes.Length)
            return false;

        var fullBytes = prefixLength / 8;
        var remainingBits = prefixLength % 8;

        for (var i = 0; i < fullBytes; i++)
        {
            if (ipBytes[i] != networkBytes[i])
                return false;
        }

        if (remainingBits > 0 && fullBytes < ipBytes.Length)
        {
            var mask = (byte)(0xFF << (8 - remainingBits));
            if ((ipBytes[fullBytes] & mask) != (networkBytes[fullBytes] & mask))
                return false;
        }

        return true;
    }

    private static bool IsPrivateNetwork(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();

        if (bytes.Length == 4) // IPv4
        {
            // 10.0.0.0/8
            if (bytes[0] == 10) return true;
            // 172.16.0.0/12
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
            // 192.168.0.0/16
            if (bytes[0] == 192 && bytes[1] == 168) return true;
            // 127.0.0.0/8 (localhost)
            if (bytes[0] == 127) return true;
        }
        else if (bytes.Length == 16) // IPv6
        {
            // ::1 (localhost)
            if (IPAddress.IsLoopback(ip)) return true;
            // fe80::/10 (link-local)
            if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80) return true;
            // fc00::/7 (unique local)
            if ((bytes[0] & 0xFE) == 0xFC) return true;
        }

        return false;
    }

    private static IPAddress? GetRemoteIpAddress(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            var firstIp = forwardedFor.Split(',').FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(firstIp) && IPAddress.TryParse(firstIp, out var ip))
            {
                return ip;
            }
        }

        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp) && IPAddress.TryParse(realIp, out var realIpAddress))
        {
            return realIpAddress;
        }

        return context.Connection.RemoteIpAddress;
    }

    private static List<(IPAddress Network, int PrefixLength)> ParseCidrs(List<string> cidrs)
    {
        var result = new List<(IPAddress, int)>();

        foreach (var cidr in cidrs)
        {
            var parts = cidr.Split('/');
            if (parts.Length == 2 &&
                IPAddress.TryParse(parts[0], out var network) &&
                int.TryParse(parts[1], out var prefixLength))
            {
                result.Add((network, prefixLength));
            }
        }

        return result;
    }

    private sealed class CachedIpAllowlistSettings
    {
        public IpAllowlistSettings Settings { get; }
        public HashSet<string> AllowedIps { get; }
        public List<(IPAddress Network, int PrefixLength)> AllowedCidrs { get; }

        public CachedIpAllowlistSettings(
            IpAllowlistSettings settings,
            HashSet<string> allowedIps,
            List<(IPAddress Network, int PrefixLength)> allowedCidrs)
        {
            Settings = settings;
            AllowedIps = allowedIps;
            AllowedCidrs = allowedCidrs;
        }
    }
}
