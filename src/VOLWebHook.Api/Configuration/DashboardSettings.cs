namespace VOLWebHook.Api.Configuration;

public sealed class DashboardSettings
{
    public const string SectionName = "Dashboard";

    /// <summary>
    /// Whether dashboard endpoints require authentication.
    /// CRITICAL: Should always be true in production.
    /// </summary>
    public bool RequireAuthentication { get; set; } = true;

    /// <summary>
    /// HTTP header name for dashboard API key.
    /// </summary>
    public string HeaderName { get; set; } = "X-Dashboard-Key";

    /// <summary>
    /// Valid API keys for dashboard access.
    /// IMPORTANT: Store these in environment variables or secrets manager, not in appsettings.json.
    /// </summary>
    public List<string> ValidApiKeys { get; set; } = new();

    /// <summary>
    /// Whether to expose sensitive configuration details in responses.
    /// Should be false in production.
    /// </summary>
    public bool ExposeConfigurationDetails { get; set; } = false;

    /// <summary>
    /// Maximum number of webhooks to return in list endpoints.
    /// </summary>
    public int MaxWebhooksPerRequest { get; set; } = 500;

    /// <summary>
    /// Maximum length of search query strings.
    /// </summary>
    public int MaxSearchLength { get; set; } = 500;
}
