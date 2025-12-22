namespace VOLWebHook.Api.Configuration;

public sealed class WebhookSettings
{
    public const string SectionName = "Webhook";

    /// <summary>
    /// Maximum size of webhook payload in bytes.
    /// Default: 10 MB. Maximum recommended: 100 MB.
    /// </summary>
    public long MaxPayloadSizeBytes { get; set; } = 10 * 1024 * 1024; // 10 MB default

    /// <summary>
    /// Whether to return 200 OK even when internal errors occur.
    /// Prevents webhook senders from retrying on our internal failures.
    /// </summary>
    public bool AlwaysReturn200 { get; set; } = true;

    /// <summary>
    /// Directory path where webhook payloads are stored.
    /// Must be absolute path or relative to application root.
    /// </summary>
    public string PayloadStoragePath { get; set; } = "./webhooks";

    /// <summary>
    /// Whether to persist webhook payloads to disk.
    /// </summary>
    public bool EnablePayloadPersistence { get; set; } = true;

    /// <summary>
    /// Number of days to retain webhook data before cleanup.
    /// </summary>
    public int PayloadRetentionDays { get; set; } = 30;

    /// <summary>
    /// Request timeout in seconds for webhook processing.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;
}
