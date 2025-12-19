using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using VOLWebHook.Api.Configuration;

namespace VOLWebHook.Api.Middleware;

public sealed class ApiKeyAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;
    private readonly ApiKeySettings _settings;
    private readonly HashSet<string> _validKeyHashes;

    public ApiKeyAuthenticationMiddleware(
        RequestDelegate next,
        IOptions<SecuritySettings> settings,
        ILogger<ApiKeyAuthenticationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _settings = settings.Value.ApiKey;

        // Store hashes of valid keys for secure comparison
        _validKeyHashes = new HashSet<string>(
            _settings.ValidKeys.Select(k => ComputeHash(k)),
            StringComparer.Ordinal);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_settings.Enabled)
        {
            await _next(context);
            return;
        }

        var providedKey = context.Request.Headers[_settings.HeaderName].FirstOrDefault();

        if (string.IsNullOrEmpty(providedKey))
        {
            _logger.LogWarning("Missing API key in request from {IpAddress}",
                context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized: API key required");
            return;
        }

        var providedKeyHash = ComputeHash(providedKey);
        if (!_validKeyHashes.Contains(providedKeyHash))
        {
            _logger.LogWarning("Invalid API key in request from {IpAddress}",
                context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized: Invalid API key");
            return;
        }

        await _next(context);
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
