using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using VOLWebHook.Api.Configuration;

namespace VOLWebHook.Api.Middleware;

public sealed class HmacSignatureMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<HmacSignatureMiddleware> _logger;
    private readonly HmacSettings _settings;

    public HmacSignatureMiddleware(
        RequestDelegate next,
        IOptions<SecuritySettings> settings,
        ILogger<HmacSignatureMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _settings = settings.Value.Hmac;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_settings.Enabled || string.IsNullOrEmpty(_settings.SharedSecret))
        {
            await _next(context);
            return;
        }

        // Request body must be buffered before this middleware runs
        if (!context.Request.Body.CanSeek)
        {
            _logger.LogWarning("Request body not buffered for HMAC verification");
            await _next(context);
            return;
        }

        var providedSignature = context.Request.Headers[_settings.HeaderName].FirstOrDefault();
        if (string.IsNullOrEmpty(providedSignature))
        {
            _logger.LogWarning("Missing HMAC signature in request from {IpAddress}",
                context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized: Signature required");
            return;
        }

        context.Request.Body.Position = 0;
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        context.Request.Body.Position = 0;

        var computedSignature = ComputeSignature(body);
        if (!SecureCompare(providedSignature, computedSignature))
        {
            _logger.LogWarning("Invalid HMAC signature in request from {IpAddress}",
                context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync("Unauthorized: Invalid signature");
            return;
        }

        await _next(context);
    }

    private string ComputeSignature(string payload)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_settings.SharedSecret!);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = _settings.Algorithm.ToUpperInvariant() switch
        {
            "HMACSHA256" => (HMAC)new HMACSHA256(keyBytes),
            "HMACSHA384" => new HMACSHA384(keyBytes),
            "HMACSHA512" => new HMACSHA512(keyBytes),
            _ => new HMACSHA256(keyBytes)
        };

        var hashBytes = hmac.ComputeHash(payloadBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static bool SecureCompare(string a, string b)
    {
        if (a.Length != b.Length)
            return false;

        var result = 0;
        for (var i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }
        return result == 0;
    }
}
