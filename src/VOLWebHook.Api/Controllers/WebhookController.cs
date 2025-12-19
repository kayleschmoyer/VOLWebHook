using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using VOLWebHook.Api.Configuration;
using VOLWebHook.Api.Models;
using VOLWebHook.Api.Services;

namespace VOLWebHook.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class WebhookController : ControllerBase
{
    private readonly IWebhookProcessingService _processingService;
    private readonly WebhookSettings _settings;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IWebhookProcessingService processingService,
        IOptions<WebhookSettings> settings,
        ILogger<WebhookController> logger)
    {
        _processingService = processingService;
        _settings = settings.Value;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N");
        Response.Headers["X-Request-Id"] = requestId;

        try
        {
            var rawBody = HttpContext.Items["RawRequestBody"] as string ?? string.Empty;
            var webhookRequest = await _processingService.ProcessAsync(HttpContext, rawBody, cancellationToken);

            Response.Headers["X-Request-Id"] = webhookRequest.Id;

            return Ok(new WebhookResponse
            {
                RequestId = webhookRequest.Id,
                ReceivedAtUtc = webhookRequest.ReceivedAtUtc
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception processing webhook. RequestId: {RequestId}", requestId);

            if (_settings.AlwaysReturn200)
            {
                return Ok(new { status = "error", requestId });
            }

            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
