using DayTrace.Bot.Configuration;
using DayTrace.Bot.Handlers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;

namespace DayTrace.Api.Controllers;

[ApiController]
[Route("bot")]
public class BotWebhookController : ControllerBase
{
    private readonly BotUpdateHandler _updateHandler;
    private readonly TelegramBotOptions _options;
    private readonly ILogger<BotWebhookController> _logger;

    public BotWebhookController(
        BotUpdateHandler updateHandler,
        IOptions<TelegramBotOptions> options,
        ILogger<BotWebhookController> logger)
    {
        _updateHandler = updateHandler;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Telegram Bot webhook endpoint. Receives updates from Telegram.
    /// Verifies X-Telegram-Bot-Api-Secret-Token header (FR-12.3).
    /// </summary>
    [HttpPost("webhook")]
    public async Task<IActionResult> HandleWebhook(
        [FromBody] Update update,
        CancellationToken cancellationToken)
    {
        // Verify secret token (FR-12.3)
        if (!VerifySecretToken())
        {
            _logger.LogWarning("Webhook request with invalid or missing secret token");
            return Unauthorized(new { error = "unauthorized", message = "Invalid or missing secret token" });
        }

        _logger.LogDebug("Processing webhook update {UpdateId}", update.Id);

        await _updateHandler.HandleUpdateAsync(update, cancellationToken);

        return Ok();
    }

    private bool VerifySecretToken()
    {
        if (string.IsNullOrEmpty(_options.WebhookSecretToken))
        {
            _logger.LogWarning("WebhookSecretToken is not configured — rejecting all webhook requests");
            return false;
        }

        var headerValue = Request.Headers["X-Telegram-Bot-Api-Secret-Token"].FirstOrDefault();

        if (string.IsNullOrEmpty(headerValue))
        {
            return false;
        }

        return string.Equals(headerValue, _options.WebhookSecretToken, StringComparison.Ordinal);
    }
}
