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
    [Consumes("application/json", "text/plain", "application/xml", "text/xml", "*/*")]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        try
        {
            var rawBody = HttpContext.Items["RawRequestBody"] as string ?? string.Empty;
            var webhookRequest = await _processingService.ProcessAsync(HttpContext, rawBody, cancellationToken);

            var response = new WebhookResponse
            {
                RequestId = webhookRequest.Id,
                ReceivedAtUtc = webhookRequest.ReceivedAtUtc,
                Status = "received"
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception processing webhook");

            if (_settings.AlwaysReturn200)
            {
                return Ok(new { status = "error", message = "Internal processing error" });
            }

            return StatusCode(StatusCodes.Status500InternalServerError,
                new { status = "error", message = "Internal server error" });
        }
    }

    [HttpGet]
    public IActionResult HealthCheck()
    {
        return Ok(new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            service = "VOLWebHook"
        });
    }
}
