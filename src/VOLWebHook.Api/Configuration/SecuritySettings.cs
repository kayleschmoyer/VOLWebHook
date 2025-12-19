namespace VOLWebHook.Api.Configuration;

public sealed class SecuritySettings
{
    public const string SectionName = "Security";

    public IpAllowlistSettings IpAllowlist { get; set; } = new();
    public ApiKeySettings ApiKey { get; set; } = new();
    public HmacSettings Hmac { get; set; } = new();
    public RateLimitSettings RateLimit { get; set; } = new();
}

public sealed class IpAllowlistSettings
{
    public bool Enabled { get; set; } = false;
    public List<string> AllowedIps { get; set; } = new();
    public List<string> AllowedCidrs { get; set; } = new();
    public bool AllowPrivateNetworks { get; set; } = true;
}

public sealed class ApiKeySettings
{
    public bool Enabled { get; set; } = false;
    public string HeaderName { get; set; } = "X-API-Key";
    public List<string> ValidKeys { get; set; } = new();
}

public sealed class HmacSettings
{
    public bool Enabled { get; set; } = false;
    public string HeaderName { get; set; } = "X-Signature";
    public string Algorithm { get; set; } = "HMACSHA256";
    public string? SharedSecret { get; set; }
}

public sealed class RateLimitSettings
{
    public bool Enabled { get; set; } = false;
    public int RequestsPerMinute { get; set; } = 100;
    public int RequestsPerHour { get; set; } = 1000;
    public bool PerIpAddress { get; set; } = true;
}
