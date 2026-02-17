using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace DayTrace.Bot.Handlers;

/// <summary>
/// Dispatches incoming Telegram updates to appropriate handlers.
/// </summary>
public class BotUpdateHandler
{
    private readonly ILogger<BotUpdateHandler> _logger;

    public BotUpdateHandler(ILogger<BotUpdateHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleUpdateAsync(Update update, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received update {UpdateId} of type {UpdateType}", update.Id, update.Type);

        try
        {
            var handler = update switch
            {
                { Message: { } message } => HandleMessageAsync(message, cancellationToken),
                { CallbackQuery: { } callbackQuery } => HandleCallbackQueryAsync(callbackQuery, cancellationToken),
                _ => HandleUnknownUpdateAsync(update, cancellationToken)
            };

            await handler;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling update {UpdateId}", update.Id);
        }
    }

    private Task HandleMessageAsync(Message message, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received message from user {UserId}: {Text}",
            message.From?.Id, message.Text?.Substring(0, Math.Min(message.Text?.Length ?? 0, 50)));

        // Will be implemented in US-042+ (command handlers, event creation, etc.)
        return Task.CompletedTask;
    }

    private Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received callback query from user {UserId}: {Data}",
            callbackQuery.From.Id, callbackQuery.Data);

        // Will be implemented in US-042+ (inline button handlers)
        return Task.CompletedTask;
    }

    private Task HandleUnknownUpdateAsync(Update update, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Received unknown update type {UpdateType}", update.Type);
        return Task.CompletedTask;
    }
}
