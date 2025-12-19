using System.Text.Json;
using VOLWebHook.Api.Models;

namespace VOLWebHook.Api.Services;

public interface IWebhookProcessingService
{
    Task<WebhookRequest> ProcessAsync(HttpContext context, string rawBody, CancellationToken cancellationToken = default);
}

public sealed class WebhookProcessingService : IWebhookProcessingService
{
    private readonly IWebhookPersistenceService _persistenceService;
    private readonly ILogger<WebhookProcessingService> _logger;

    public WebhookProcessingService(
        IWebhookPersistenceService persistenceService,
        ILogger<WebhookProcessingService> logger)
    {
        _persistenceService = persistenceService;
        _logger = logger;
    }

    public async Task<WebhookRequest> ProcessAsync(HttpContext context, string rawBody, CancellationToken cancellationToken = default)
    {
        var request = context.Request;
        var connection = context.Connection;

        var (isValidJson, parseError) = ValidateJson(rawBody);

        var headers = new Dictionary<string, string[]>();
        foreach (var header in request.Headers)
        {
            headers[header.Key] = header.Value.ToArray();
        }

        var webhookRequest = new WebhookRequest
        {
            ReceivedAtUtc = DateTime.UtcNow,
            HttpMethod = request.Method,
            Path = request.Path.Value ?? "/",
            QueryString = request.QueryString.HasValue ? request.QueryString.Value : null,
            SourceIpAddress = GetClientIpAddress(context),
            SourcePort = connection.RemotePort,
            Headers = headers,
            RawBody = rawBody,
            ContentLength = rawBody.Length,
            ContentType = request.ContentType,
            IsValidJson = isValidJson,
            JsonParseError = parseError
        };

        _logger.LogInformation("Webhook received: {LogString}", webhookRequest.ToLogString());

        try
        {
            await _persistenceService.SaveAsync(webhookRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist webhook {RequestId}", webhookRequest.Id);
        }

        return webhookRequest;
    }

    private static (bool isValid, string? error) ValidateJson(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return (false, null);

        try
        {
            using var document = JsonDocument.Parse(content);
            return (true, null);
        }
        catch (JsonException ex)
        {
            return (false, ex.Message);
        }
    }

    private static string GetClientIpAddress(HttpContext context)
    {
        // Check for forwarded headers (reverse proxy scenarios)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // X-Forwarded-For can contain multiple IPs; take the first one
            var firstIp = forwardedFor.Split(',').FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(firstIp))
            {
                return firstIp;
            }
        }

        var realIp = context.Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
