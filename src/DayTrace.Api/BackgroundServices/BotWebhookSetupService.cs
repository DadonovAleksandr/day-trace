using DayTrace.Bot.Configuration;
using Microsoft.Extensions.Options;
using Telegram.Bot;

namespace DayTrace.Api.BackgroundServices;

/// <summary>
/// Registers the Telegram webhook on application startup when WebhookBaseUrl is configured.
/// Runs once and completes — not a long-running background service.
/// </summary>
public class BotWebhookSetupService : IHostedService
{
    private readonly ITelegramBotClient _botClient;
    private readonly TelegramBotOptions _options;
    private readonly ILogger<BotWebhookSetupService> _logger;

    public BotWebhookSetupService(
        ITelegramBotClient botClient,
        IOptions<TelegramBotOptions> options,
        ILogger<BotWebhookSetupService> logger)
    {
        _botClient = botClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.WebhookBaseUrl))
        {
            _logger.LogDebug("WebhookBaseUrl is not configured — skipping webhook registration");
            return;
        }

        var webhookUrl = $"{_options.WebhookBaseUrl.TrimEnd('/')}/bot/webhook";

        try
        {
            await _botClient.SetWebhook(
                url: webhookUrl,
                secretToken: _options.WebhookSecretToken,
                cancellationToken: cancellationToken);

            _logger.LogInformation("Telegram webhook registered: {WebhookUrl}", webhookUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register Telegram webhook at {WebhookUrl}", webhookUrl);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
