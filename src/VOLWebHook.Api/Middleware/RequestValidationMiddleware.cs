using Microsoft.Extensions.Options;
using VOLWebHook.Api.Configuration;

namespace VOLWebHook.Api.Middleware;

/// <summary>
/// Validates incoming HTTP requests to prevent malformed or malicious requests.
/// </summary>
public sealed class RequestValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestValidationMiddleware> _logger;
    private readonly IOptionsMonitor<WebhookSettings> _webhookSettings;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/json",
        "application/x-www-form-urlencoded",
        "text/plain",
        "application/octet-stream"
    };

    public RequestValidationMiddleware(
        RequestDelegate next,
        IOptionsMonitor<WebhookSettings> webhookSettings,
        ILogger<RequestValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _webhookSettings = webhookSettings;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Validate Content-Length is not negative (CVE-2022-24761 style attack)
        if (context.Request.ContentLength.HasValue && context.Request.ContentLength.Value < 0)
        {
            _logger.LogWarning("Request with negative Content-Length from {IpAddress}", context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Invalid Content-Length header");
            return;
        }

        // Validate Content-Type for POST requests
        if (context.Request.Method == HttpMethods.Post && !string.IsNullOrEmpty(context.Request.ContentType))
        {
            var contentType = context.Request.ContentType.Split(';')[0].Trim();
            if (!AllowedContentTypes.Contains(contentType))
            {
                _logger.LogWarning("Request with unsupported Content-Type: {ContentType} from {IpAddress}",
                    contentType, context.Connection.RemoteIpAddress);
                context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                await context.Response.WriteAsync("Unsupported Media Type. Allowed: application/json, text/plain, application/x-www-form-urlencoded");
                return;
            }
        }

        // Validate request path for directory traversal attempts
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.Contains("..") || path.Contains("%2e%2e", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Path traversal attempt detected from {IpAddress}: {Path}",
                context.Connection.RemoteIpAddress, path);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Invalid request path");
            return;
        }

        // Validate query string length to prevent DoS
        if (context.Request.QueryString.HasValue &&
            context.Request.QueryString.Value!.Length > 4096)
        {
            _logger.LogWarning("Excessive query string length from {IpAddress}: {Length}",
                context.Connection.RemoteIpAddress, context.Request.QueryString.Value.Length);
            context.Response.StatusCode = StatusCodes.Status414UriTooLong;
            await context.Response.WriteAsync("Query string too long");
            return;
        }

        // Validate header count to prevent header bomb attacks
        if (context.Request.Headers.Count > 100)
        {
            _logger.LogWarning("Excessive header count from {IpAddress}: {Count}",
                context.Connection.RemoteIpAddress, context.Request.Headers.Count);
            context.Response.StatusCode = StatusCodes.Status431RequestHeaderFieldsTooLarge;
            await context.Response.WriteAsync("Too many headers");
            return;
        }

        // Validate individual header lengths
        foreach (var header in context.Request.Headers)
        {
            if (header.Key.Length > 256 || header.Value.ToString().Length > 8192)
            {
                _logger.LogWarning("Excessive header size from {IpAddress}: {HeaderName}",
                    context.Connection.RemoteIpAddress, header.Key);
                context.Response.StatusCode = StatusCodes.Status431RequestHeaderFieldsTooLarge;
                await context.Response.WriteAsync("Header field too large");
                return;
            }
        }

        await _next(context);
    }
}
