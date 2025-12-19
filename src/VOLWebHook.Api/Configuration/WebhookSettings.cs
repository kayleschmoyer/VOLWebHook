namespace VOLWebHook.Api.Configuration;

public sealed class WebhookSettings
{
    public const string SectionName = "Webhook";

    public int MaxPayloadSizeBytes { get; set; } = 10 * 1024 * 1024; // 10 MB default
    public bool AlwaysReturn200 { get; set; } = true;
    public string PayloadStoragePath { get; set; } = "./webhooks";
    public bool EnablePayloadPersistence { get; set; } = true;
    public int PayloadRetentionDays { get; set; } = 30;
}
