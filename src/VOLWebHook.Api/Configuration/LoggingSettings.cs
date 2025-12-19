namespace VOLWebHook.Api.Configuration;

public sealed class LoggingSettings
{
    public const string SectionName = "FileLogging";

    public bool Enabled { get; set; } = true;
    public string LogDirectory { get; set; } = "./logs";
    public string FileNamePattern { get; set; } = "webhook-{date}.log";
    public long MaxFileSizeBytes { get; set; } = 100 * 1024 * 1024;
}
