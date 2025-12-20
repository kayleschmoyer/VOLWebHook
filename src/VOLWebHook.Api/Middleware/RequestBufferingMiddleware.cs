using System.Text;
using Microsoft.Extensions.Options;
using VOLWebHook.Api.Configuration;

namespace VOLWebHook.Api.Middleware;

public sealed class RequestBufferingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestBufferingMiddleware> _logger;
    private readonly IOptionsMonitor<WebhookSettings> _webhookSettings;

    public RequestBufferingMiddleware(
        RequestDelegate next,
        IOptionsMonitor<WebhookSettings> settings,
        ILogger<RequestBufferingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _webhookSettings = settings;

        // Log configuration changes
        settings.OnChange(newSettings =>
        {
            _logger.LogInformation(
                "Webhook settings reloaded: MaxPayloadSize={MaxSize}",
                newSettings.MaxPayloadSizeBytes);
        });
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var settings = _webhookSettings.CurrentValue;

        // Check content length before buffering
        if (context.Request.ContentLength > settings.MaxPayloadSizeBytes)
        {
            _logger.LogWarning("Request payload too large: {Size} bytes (max: {MaxSize})",
                context.Request.ContentLength, settings.MaxPayloadSizeBytes);
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsync($"Payload too large. Maximum size: {settings.MaxPayloadSizeBytes} bytes");
            return;
        }

        // Enable buffering so the body can be read multiple times
        context.Request.EnableBuffering();

        // Read the body to check actual size (for chunked transfers)
        string body;
        using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            body = await reader.ReadToEndAsync();
        }
        context.Request.Body.Position = 0;

        // Check byte size, not character count (important for multi-byte characters)
        var byteSize = Encoding.UTF8.GetByteCount(body);
        if (byteSize > settings.MaxPayloadSizeBytes)
        {
            _logger.LogWarning("Request payload too large after reading: {Size} bytes (max: {MaxSize})",
                byteSize, settings.MaxPayloadSizeBytes);
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsync($"Payload too large. Maximum size: {settings.MaxPayloadSizeBytes} bytes");
            return;
        }

        // Store the raw body in HttpContext.Items for downstream use
        context.Items["RawRequestBody"] = body;

        await _next(context);
    }
}
