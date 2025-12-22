namespace VOLWebHook.Api.Configuration;

/// <summary>
/// Configuration for trusted proxies and load balancers.
/// CRITICAL: Misconfiguration allows IP spoofing and security bypass.
/// </summary>
public sealed class ForwardedHeadersConfig
{
    public const string SectionName = "ForwardedHeaders";

    /// <summary>
    /// Whether to trust X-Forwarded-* headers.
    /// Should only be true when behind a reverse proxy.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// List of trusted proxy IP addresses.
    /// Only headers from these IPs will be trusted.
    /// </summary>
    public List<string> TrustedProxies { get; set; } = new();

    /// <summary>
    /// List of trusted proxy CIDR ranges.
    /// </summary>
    public List<string> TrustedNetworks { get; set; } = new();

    /// <summary>
    /// Number of proxies in the chain.
    /// Used to determine which IP to trust from X-Forwarded-For.
    /// </summary>
    public int ForwardLimit { get; set; } = 1;

    /// <summary>
    /// Whether to trust X-Forwarded-For header.
    /// </summary>
    public bool ForwardedForEnabled { get; set; } = true;

    /// <summary>
    /// Whether to trust X-Forwarded-Proto header.
    /// </summary>
    public bool ForwardedProtoEnabled { get; set; } = true;

    /// <summary>
    /// Whether to trust X-Forwarded-Host header.
    /// </summary>
    public bool ForwardedHostEnabled { get; set; } = false;
}
