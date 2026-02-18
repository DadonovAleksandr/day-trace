using DayTrace.Bot.Configuration;
using DayTrace.Bot.Handlers;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DayTrace.Api.BackgroundServices;

/// <summary>
/// Long-polling background service for local development.
/// Active only when WebhookBaseUrl is empty.
/// </summary>
public class BotPollingService : BackgroundService
{
    private readonly ITelegramBotClient _botClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly TelegramBotOptions _options;
    private readonly ILogger<BotPollingService> _logger;

    public BotPollingService(
        ITelegramBotClient botClient,
        IServiceProvider serviceProvider,
        IOptions<TelegramBotOptions> options,
        ILogger<BotPollingService> logger)
    {
        _botClient = botClient;
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!string.IsNullOrEmpty(_options.WebhookBaseUrl))
        {
            _logger.LogInformation("WebhookBaseUrl is configured — polling disabled, using webhook mode");
            return;
        }

        // Delete any existing webhook so polling works
        await _botClient.DeleteWebhook(cancellationToken: stoppingToken);
        _logger.LogInformation("Bot polling started (webhook removed)");

        int? offset = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await _botClient.GetUpdates(
                    offset: offset,
                    timeout: 30,
                    cancellationToken: stoppingToken);

                foreach (var update in updates)
                {
                    offset = update.Id + 1;

                    try
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var handler = scope.ServiceProvider.GetRequiredService<BotUpdateHandler>();
                        await handler.HandleUpdateAsync(update, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing update {UpdateId}", update.Id);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Polling error, retrying in 5s");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("Bot polling stopped");
    }
}
