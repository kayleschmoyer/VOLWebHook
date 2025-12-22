using System.Text.Json.Serialization;

namespace VOLWebHook.Api.Models;

public sealed class WebhookRequest
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTime ReceivedAtUtc { get; init; } = DateTime.UtcNow;
    public string HttpMethod { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string? QueryString { get; init; }
    public string SourceIpAddress { get; init; } = string.Empty;
    public int SourcePort { get; init; }
    public Dictionary<string, string[]> Headers { get; init; } = new();
    public string RawBody { get; init; } = string.Empty;
    public int ContentLength { get; init; }
    public string? ContentType { get; init; }
    public bool IsValidJson { get; init; }
    public string? JsonParseError { get; init; }

    [JsonIgnore]
    public string FileName => $"{ReceivedAtUtc:yyyyMMdd_HHmmss_fff}_{Id}.json";

    public string ToLogString()
    {
        var payloadPreview = string.IsNullOrEmpty(RawBody)
            ? "(empty)"
            : RawBody.Length > 500
                ? RawBody.Substring(0, 500) + "..."
                : RawBody;

        return $"[{Id}] {ReceivedAtUtc:yyyy-MM-dd HH:mm:ss.fff} UTC | {HttpMethod} {Path} | Source: {SourceIpAddress}:{SourcePort} | Size: {ContentLength} bytes | Valid JSON: {IsValidJson} | Payload: {payloadPreview}";
    }
}

public sealed class WebhookResponse
{
    public string RequestId { get; init; } = string.Empty;
    public DateTime ReceivedAtUtc { get; init; }
    public string Status { get; init; } = "received";
}
