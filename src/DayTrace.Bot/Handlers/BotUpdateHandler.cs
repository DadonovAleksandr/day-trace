using System.Collections.Concurrent;
using DayTrace.Bot.Configuration;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.Payments;
using Telegram.Bot.Types.ReplyMarkups;

namespace DayTrace.Bot.Handlers;

/// <summary>
/// Dispatches incoming Telegram updates to appropriate handlers (US-042, US-043, US-044, US-045).
/// </summary>
public class BotUpdateHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly TelegramBotOptions _options;
    private readonly UserRegistrationService _registrationService;
    private readonly IUserSettingsRepository _settingsRepo;
    private readonly IUserFeedbackRepository _feedbackRepo;
    private readonly SubscriptionService _subscriptionService;
    private readonly ILogger<BotUpdateHandler> _logger;

    // Anti-double-submit: tracks recent callback timestamps per (chatId, data)
    private static readonly ConcurrentDictionary<string, DateTime> RecentCallbacks = new();

    public BotUpdateHandler(
        ITelegramBotClient botClient,
        IOptions<TelegramBotOptions> options,
        UserRegistrationService registrationService,
        IUserSettingsRepository settingsRepo,
        IUserFeedbackRepository feedbackRepo,
        SubscriptionService subscriptionService,
        ILogger<BotUpdateHandler> logger)
    {
        _botClient = botClient;
        _options = options.Value;
        _registrationService = registrationService;
        _settingsRepo = settingsRepo;
        _feedbackRepo = feedbackRepo;
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    public async Task HandleUpdateAsync(Update update, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Received update {UpdateId} of type {UpdateType}", update.Id, update.Type);

        // Periodically clean stale dedup entries on every update to prevent unbounded growth
        CleanRecentCallbacks();

        try
        {
            var handler = update switch
            {
                { PreCheckoutQuery: { } pcq } => HandlePreCheckoutQueryAsync(pcq, cancellationToken),
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

    private async Task HandleMessageAsync(Message message, CancellationToken ct)
    {
        if (message.SuccessfulPayment is not null)
        {
            await HandleSuccessfulPaymentAsync(message, ct);
            return;
        }

        if (message.From == null) return;

        var telegramUserId = message.From.Id;
        var text = message.Text?.Trim() ?? string.Empty;

        _logger.LogInformation("Received message from user {UserId}: {Text}",
            telegramUserId, text.Length > 50 ? text[..50] : text);

        // Command routing
        var command = text.Split(' ', '@').FirstOrDefault()?.ToLowerInvariant();
        switch (command)
        {
            case "/start":
                await HandleStartCommandAsync(message, ct);
                break;
            case "/help":
                await HandleHelpCommandAsync(message, ct);
                break;
            default:
                // Unrecognized text — treat as potential event text (US-043 will handle)
                await HandleUnrecognizedTextAsync(message, ct);
                break;
        }
    }

    /// <summary>
    /// /start → register user, send welcome message with quick action menu (US-042).
    /// </summary>
    private async Task HandleStartCommandAsync(Message message, CancellationToken ct)
    {
        var telegramUserId = message.From!.Id;

        var (user, isNew) = await _registrationService.RegisterAsync(telegramUserId, null, ct);

        var welcomeText = isNew
            ? "👋 *Добро пожаловать в Событник!*"
            : "👋 *С возвращением в Событник!*";

        welcomeText +=
            "\n\n" +
            "Событник — ваш личный дневник событий. " +
            "Записывайте важные моменты в приложении, а я сформирую итоги за неделю, месяц и год.\n\n" +
            "📱 Откройте приложение, чтобы записать события дня\n" +
            "📊 Получайте автоматические итоги\n\n" +
            "🌱 Когда вы замечаете и фиксируете то, что происходит — жизнь становится осмысленнее.";

        if (user.Settings?.Timezone == "UTC")
        {
            welcomeText += "\n\n💡 _Откройте приложение, чтобы определить ваш часовой пояс автоматически._";
        }

        await _botClient.SendMessage(
            chatId: message.Chat.Id,
            text: welcomeText,
            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
            replyMarkup: GetQuickActionKeyboard(),
            cancellationToken: ct);
    }

    /// <summary>
    /// /help — show help message.
    /// </summary>
    private async Task HandleHelpCommandAsync(Message message, CancellationToken ct)
    {
        var helpText =
            "📖 *Как пользоваться Событником:*\n\n" +
            "• Откройте приложение — записывайте события дня\n" +
            "• /start — главное меню\n" +
            "• /help — эта справка\n\n" +
            "Откройте приложение по кнопке ниже:";

        await _botClient.SendMessage(
            chatId: message.Chat.Id,
            text: helpText,
            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
            replyMarkup: GetQuickActionKeyboard(),
            cancellationToken: ct);
    }

    /// <summary>
    /// Unrecognized text → save as user feedback.
    /// </summary>
    private async Task HandleUnrecognizedTextAsync(Message message, CancellationToken ct)
    {
        var text = message.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        var telegramUserId = message.From!.Id;

        try
        {
            var (user, _) = await _registrationService.RegisterAsync(telegramUserId, null, ct);

            var feedbackText = text.Length > 2000 ? text[..2000] : text;
            var feedback = new UserFeedback
            {
                UserId = user.Id,
                Text = feedbackText,
                Status = "new",
                CreatedAt = DateTime.UtcNow
            };
            await _feedbackRepo.CreateAsync(feedback, ct);

            _logger.LogInformation("Saved feedback {FeedbackId} from user {UserId}", feedback.Id, user.Id);

            await _botClient.SendMessage(
                chatId: message.Chat.Id,
                text: "💬 Спасибо за обратную связь! Мы получили ваше сообщение.",
                replyMarkup: GetQuickActionKeyboard(),
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save feedback from telegram user {TelegramUserId}", telegramUserId);

            await _botClient.SendMessage(
                chatId: message.Chat.Id,
                text: "❌ Не удалось сохранить сообщение. Попробуйте позже.",
                cancellationToken: ct);
        }
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        _logger.LogInformation("Received callback query from user {UserId}: {Data}",
            callbackQuery.From.Id, callbackQuery.Data);

        var data = callbackQuery.Data ?? string.Empty;
        var chatId = callbackQuery.Message?.Chat.Id ?? callbackQuery.From.Id;

        // Anti-double-submit: ignore repeated clicks within 3 seconds (FR-9)
        var dedupeKey = $"{chatId}_{data}";
        if (RecentCallbacks.TryGetValue(dedupeKey, out var lastTime) &&
            (DateTime.UtcNow - lastTime).TotalSeconds < 3)
        {
            await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
            return;
        }
        RecentCallbacks[dedupeKey] = DateTime.UtcNow;

        // Acknowledge the callback
        await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);

        // Route callbacks
        if (data.StartsWith("summary_"))
        {
            await HandleSummaryCallbackAsync(callbackQuery, data, ct);
        }
        else
        {
            _logger.LogDebug("Unhandled callback data: {Data}", data);
        }
    }

    /// <summary>
    /// Handle summary-related callbacks — redirect to mini app.
    /// </summary>
    private async Task HandleSummaryCallbackAsync(CallbackQuery query, string data, CancellationToken ct)
    {
        var chatId = query.Message?.Chat.Id ?? query.From.Id;

        await _botClient.SendMessage(
            chatId: chatId,
            text: "📊 Выберите главное событие периода в приложении:",
            replyMarkup: GetQuickActionKeyboard(),
            cancellationToken: ct);
    }

    private static void CleanRecentCallbacks()
    {
        // Remove entries older than 5 minutes — far beyond the 3-second dedup window
        var cutoff = DateTime.UtcNow.AddMinutes(-5);
        foreach (var key in RecentCallbacks.Keys.ToArray())
        {
            if (RecentCallbacks.TryGetValue(key, out var ts) && ts < cutoff)
                RecentCallbacks.TryRemove(key, out _);
        }
    }

    private async Task HandlePreCheckoutQueryAsync(PreCheckoutQuery pcq, CancellationToken ct)
    {
        _logger.LogInformation("Answering pre-checkout query {QueryId} for payload {Payload}", pcq.Id, pcq.InvoicePayload);
        await _botClient.AnswerPreCheckoutQuery(pcq.Id, cancellationToken: ct);
    }

    private async Task HandleSuccessfulPaymentAsync(Message message, CancellationToken ct)
    {
        var payment = message.SuccessfulPayment!;
        var payload = payment.InvoicePayload;
        var fromUserId = message.From!.Id;

        _logger.LogInformation("Received successful payment: charge_id={ChargeId}, payload={Payload}",
            payment.TelegramPaymentChargeId, payload);

        var parts = payload.Split('_');
        if (parts.Length != 4 || parts[0] != "sub")
        {
            _logger.LogWarning("Invalid payment payload format: {Payload}", payload);
            return;
        }

        var plan = parts[1];
        if (plan is not ("monthly" or "annual"))
        {
            _logger.LogWarning("Invalid plan in payload: {Plan}", plan);
            return;
        }

        if (!long.TryParse(parts[2], out var userId))
        {
            _logger.LogWarning("Invalid userId in payload: {Part}", parts[2]);
            return;
        }

        if (userId != fromUserId)
        {
            _logger.LogWarning("Suspicious payment: payload userId mismatch. From={From}, Payload={Payload}", fromUserId, payload);
            return;
        }

        bool activated;
        try
        {
            activated = await _subscriptionService.ActivateAsync(userId, plan, payment.TelegramPaymentChargeId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate subscription for user {UserId}, charge_id={ChargeId}", userId, payment.TelegramPaymentChargeId);
            return;
        }

        if (activated)
        {
            await _botClient.SendMessage(
                message.Chat.Id,
                "Подписка активирована! Спасибо за оплату.",
                cancellationToken: ct);

            _logger.LogInformation("Subscription activated for user {UserId}, plan={Plan}", userId, plan);
        }
        else
        {
            _logger.LogInformation("Payment already processed (dedup): charge_id={ChargeId}", payment.TelegramPaymentChargeId);
        }
    }

    private Task HandleUnknownUpdateAsync(Update update, CancellationToken ct)
    {
        _logger.LogDebug("Received unknown update type {UpdateType}", update.Type);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Quick action inline keyboard with WebApp button to open the Mini App.
    /// </summary>
    private InlineKeyboardMarkup GetQuickActionKeyboard()
    {
        var miniAppUrl = !string.IsNullOrEmpty(_options.MiniAppUrl)
            ? _options.MiniAppUrl
            : _options.WebhookBaseUrl;

        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithWebApp("🚀 Открыть", new WebAppInfo { Url = miniAppUrl }),
            },
        });
    }
}
