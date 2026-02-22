using System.Collections.Concurrent;
using DayTrace.Bot.Configuration;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
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
    private readonly IEventRepository _eventRepo;
    private readonly DateCalculationService _dateService;
    private readonly AutoTriggerService _autoTriggerService;
    private readonly PeriodJobCreationService _periodJobService;
    private readonly PeriodSelectionService _periodSelectionService;
    private readonly IUserSettingsRepository _settingsRepo;
    private readonly ILogger<BotUpdateHandler> _logger;

    // In-memory pending events: chatId → (text, timestamp)
    // Auto-expires entries older than 5 minutes on access
    private static readonly ConcurrentDictionary<long, (string Text, DateTime CreatedAt)> PendingEvents = new();

    // Anti-double-submit: tracks recent callback timestamps per (chatId, data)
    private static readonly ConcurrentDictionary<string, DateTime> RecentCallbacks = new();

    public BotUpdateHandler(
        ITelegramBotClient botClient,
        IOptions<TelegramBotOptions> options,
        UserRegistrationService registrationService,
        IEventRepository eventRepo,
        DateCalculationService dateService,
        AutoTriggerService autoTriggerService,
        PeriodJobCreationService periodJobService,
        PeriodSelectionService periodSelectionService,
        IUserSettingsRepository settingsRepo,
        ILogger<BotUpdateHandler> logger)
    {
        _botClient = botClient;
        _options = options.Value;
        _registrationService = registrationService;
        _eventRepo = eventRepo;
        _dateService = dateService;
        _autoTriggerService = autoTriggerService;
        _periodJobService = periodJobService;
        _periodSelectionService = periodSelectionService;
        _settingsRepo = settingsRepo;
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

    private async Task HandleMessageAsync(Message message, CancellationToken ct)
    {
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
            case "/settings":
                await HandleSettingsCommandAsync(message, ct);
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
            "Записывайте важные моменты, а я сформирую итоги за неделю, месяц и год.\n\n" +
            "✏️ Отправьте текст — сохраню как событие\n" +
            "⭐ Оцените важность от ★ до ★★★★★\n" +
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
            "📖 *Как пользоваться DayTrace:*\n\n" +
            "• Отправьте текст — я предложу создать событие\n" +
            "• /start — главное меню\n" +
            "• /settings — настройки\n" +
            "• /help — эта справка\n\n" +
            "Или используйте кнопки ниже:";

        await _botClient.SendMessage(
            chatId: message.Chat.Id,
            text: helpText,
            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
            replyMarkup: GetQuickActionKeyboard(),
            cancellationToken: ct);
    }

    /// <summary>
    /// /settings — show current settings with inline buttons (US-045).
    /// </summary>
    private async Task HandleSettingsCommandAsync(Message message, CancellationToken ct)
    {
        // Ensure user exists — use From.Id if available, otherwise Chat.Id (for callback-relayed messages)
        var telegramUserId = message.From?.Id ?? message.Chat.Id;
        var (user, _) = await _registrationService.RegisterAsync(telegramUserId, null, ct);
        var settings = user.Settings;

        var settingsText = settings != null
            ? $"⚙️ *Ваши настройки:*\n\n" +
              $"🕐 Часовой пояс: `{settings.Timezone}`\n" +
              $"⏰ Время напоминания: `{settings.ReminderTime:HH\\:mm}`\n" +
              $"🔔 Напоминания: {(settings.ReminderEnabled ? "включены ✅" : "выключены ❌")}\n" +
              $"📅 Конец недели: `{settings.WeekEnd}`"
            : "⚙️ Настройки не найдены. Попробуйте /start.";

        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("🕐 Часовой пояс", "settings_timezone"),
                InlineKeyboardButton.WithCallbackData("⏰ Время напоминания", "settings_reminder_time"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    settings?.ReminderEnabled == true ? "🔕 Выключить напоминания" : "🔔 Включить напоминания",
                    "settings_toggle_reminder"),
                InlineKeyboardButton.WithCallbackData("📅 Конец недели", "settings_week_end"),
            }
        });

        await _botClient.SendMessage(
            chatId: message.Chat.Id,
            text: settingsText,
            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
            replyMarkup: keyboard,
            cancellationToken: ct);
    }

    /// <summary>
    /// Unrecognized text → treat as event text, ask for importance (US-043).
    /// </summary>
    private async Task HandleUnrecognizedTextAsync(Message message, CancellationToken ct)
    {
        var text = message.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        // Validate text length
        if (text.Length > 500)
        {
            await _botClient.SendMessage(
                chatId: message.Chat.Id,
                text: $"❌ Текст слишком длинный ({text.Length}/500). Сократите и отправьте снова.",
                cancellationToken: ct);
            return;
        }

        // Store pending event text
        var chatId = message.Chat.Id;
        PendingEvents[chatId] = (text, DateTime.UtcNow);

        // Ask for importance via inline keyboard
        var keyboard = new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("★", "importance_1"),
                InlineKeyboardButton.WithCallbackData("★★", "importance_2"),
                InlineKeyboardButton.WithCallbackData("★★★", "importance_3"),
                InlineKeyboardButton.WithCallbackData("★★★★", "importance_4"),
                InlineKeyboardButton.WithCallbackData("★★★★★", "importance_5"),
            }
        });

        await _botClient.SendMessage(
            chatId: chatId,
            text: $"📝 Событие: «{(text.Length > 100 ? text[..100] + "..." : text)}»\n\nВыберите важность:",
            replyMarkup: keyboard,
            cancellationToken: ct);
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

        // Clean old entries periodically
        CleanRecentCallbacks();

        // Acknowledge the callback
        await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);

        // Route callbacks
        if (data.StartsWith("importance_"))
        {
            await HandleImportanceCallbackAsync(callbackQuery, data, ct);
        }
        else if (data.StartsWith("summary_"))
        {
            await HandleSummaryCallbackAsync(callbackQuery, data, ct);
        }
        else if (data.StartsWith("settings_"))
        {
            await HandleSettingsCallbackAsync(callbackQuery, data, ct);
        }
        else if (data == "add_event")
        {
            await _botClient.SendMessage(chatId: chatId,
                text: "📝 Отправьте текст события (до 500 символов):",
                cancellationToken: ct);
        }
        else if (data == "show_settings")
        {
            // Reuse settings command flow
            if (callbackQuery.Message != null)
            {
                var fakeMsg = callbackQuery.Message;
                await HandleSettingsCommandAsync(fakeMsg, ct);
            }
        }
        else
        {
            _logger.LogDebug("Unhandled callback data: {Data}", data);
        }
    }

    /// <summary>
    /// Handle importance selection for event creation (US-043).
    /// </summary>
    private async Task HandleImportanceCallbackAsync(CallbackQuery query, string data, CancellationToken ct)
    {
        var chatId = query.Message?.Chat.Id ?? query.From.Id;
        var telegramUserId = query.From.Id;

        // Parse importance from callback data
        if (!int.TryParse(data.Replace("importance_", ""), out var importance) ||
            importance < 1 || importance > 5)
        {
            await _botClient.SendMessage(chatId: chatId,
                text: "❌ Некорректное значение важности.", cancellationToken: ct);
            return;
        }

        // Get pending event text
        if (!PendingEvents.TryRemove(chatId, out var pending) ||
            (DateTime.UtcNow - pending.CreatedAt).TotalMinutes > 5)
        {
            await _botClient.SendMessage(chatId: chatId,
                text: "❌ Текст события не найден или истёк. Отправьте текст заново.",
                cancellationToken: ct);
            return;
        }

        // Ensure user is registered
        var (user, _) = await _registrationService.RegisterAsync(telegramUserId, null, ct);
        var settings = await _settingsRepo.GetByUserIdAsync(user.Id, ct);
        var timezone = settings?.Timezone ?? "UTC";

        var todayLocal = _dateService.GetTodayLocal(timezone);
        var stars = new string('★', importance);

        // Check if event already exists for today
        var existingEvent = await _eventRepo.GetByUserAndDateAsync(user.Id, todayLocal, ct);
        if (existingEvent != null)
        {
            // Update existing event
            existingEvent.Text = pending.Text;
            existingEvent.Importance = importance;
            existingEvent.UpdatedAt = DateTime.UtcNow;
            await _eventRepo.UpdateAsync(existingEvent, ct);

            await _botClient.SendMessage(
                chatId: chatId,
                text: $"✏️ Событие дня обновлено!\n\n" +
                      $"📝 {pending.Text}\n" +
                      $"⭐ Важность: {stars}\n" +
                      $"📅 Дата: {todayLocal:yyyy-MM-dd}",
                cancellationToken: ct);

            _logger.LogInformation("Bot event updated: event_id={EventId}, user_id={UserId}",
                existingEvent.Id, user.Id);
        }
        else
        {
            // Create new event
            var evt = new Event
            {
                UserId = user.Id,
                Text = pending.Text,
                Importance = importance,
                LocalDate = todayLocal,
                CreatedAt = DateTime.UtcNow
            };

            evt = await _eventRepo.CreateAsync(evt, ct);

            // Auto-trigger
            await _autoTriggerService.CheckAndTriggerAsync(evt, ct);

            await _botClient.SendMessage(
                chatId: chatId,
                text: $"✅ Событие записано!\n\n" +
                      $"📝 {pending.Text}\n" +
                      $"⭐ Важность: {stars}\n" +
                      $"📅 Дата: {todayLocal:yyyy-MM-dd}",
                cancellationToken: ct);

            _logger.LogInformation("Bot event created: event_id={EventId}, user_id={UserId}",
                evt.Id, user.Id);
        }
    }

    /// <summary>
    /// Handle summary generation callbacks (US-044).
    /// </summary>
    private async Task HandleSummaryCallbackAsync(CallbackQuery query, string data, CancellationToken ct)
    {
        var chatId = query.Message?.Chat.Id ?? query.From.Id;
        var telegramUserId = query.From.Id;

        // Parse period type
        var periodType = data.Replace("summary_", "");
        if (periodType is not ("weekly" or "monthly" or "yearly"))
        {
            await _botClient.SendMessage(chatId: chatId,
                text: "❌ Неизвестный тип периода.", cancellationToken: ct);
            return;
        }

        // Show progress
        var progressMsg = await _botClient.SendMessage(
            chatId: chatId,
            text: "⏳ Формируем...",
            cancellationToken: ct);

        try
        {
            // Ensure user exists
            var (user, _) = await _registrationService.RegisterAsync(telegramUserId, null, ct);
            var settings = await _settingsRepo.GetByUserIdAsync(user.Id, ct);
            var timezone = settings?.Timezone ?? "UTC";

            // Select period
            var selection = await _periodSelectionService.SelectPeriodAsync(
                user.Id, periodType, timezone, ct);

            // Create period job (force re-run mode)
            var result = await _periodJobService.CreateAsync(
                user.Id, periodType, selection.PeriodStart, selection.PeriodEnd,
                PeriodJobCreationService.CreateMode.ForceRerun, ct);

            if (!result.Success)
            {
                var errorText = result.Reason switch
                {
                    "empty_period" => "В периоде нет событий.",
                    _ => $"Ошибка: {result.Reason}"
                };

                await _botClient.EditMessageText(
                    chatId: chatId,
                    messageId: progressMsg.MessageId,
                    text: $"❌ {errorText}",
                    cancellationToken: ct);
                return;
            }

            await _botClient.EditMessageText(
                chatId: chatId,
                messageId: progressMsg.MessageId,
                text: $"✅ Задание создано!\n\n" +
                      $"📊 Тип: {periodType}\n" +
                      $"📅 Период: {selection.PeriodStart:yyyy-MM-dd} — {selection.PeriodEnd:yyyy-MM-dd}\n" +
                      $"🔄 Статус: формируется...\n\n" +
                      $"Итог будет доступен в приложении через несколько секунд.",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bot summary generation failed for user {UserId}", telegramUserId);

            await _botClient.EditMessageText(
                chatId: chatId,
                messageId: progressMsg.MessageId,
                text: "❌ Произошла ошибка при формировании итога. Попробуйте позже.",
                cancellationToken: ct);
        }
    }

    /// <summary>
    /// Handle settings-related callbacks (US-045).
    /// </summary>
    private async Task HandleSettingsCallbackAsync(CallbackQuery query, string data, CancellationToken ct)
    {
        var chatId = query.Message?.Chat.Id ?? query.From.Id;
        var telegramUserId = query.From.Id;

        switch (data)
        {
            case "settings_timezone":
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: "🕐 Для автоматического определения часового пояса откройте приложение.\n\n" +
                          "Или укажите вручную (например, Europe/Moscow, America/New_York) через приложение → Настройки.",
                    cancellationToken: ct);
                break;

            case "settings_toggle_reminder":
            {
                var (user, _) = await _registrationService.RegisterAsync(telegramUserId, null, ct);
                var settings = await _settingsRepo.GetByUserIdAsync(user.Id, ct);
                if (settings != null)
                {
                    settings.ReminderEnabled = !settings.ReminderEnabled;
                    await _settingsRepo.UpdateAsync(settings, ct);
                    var statusText = settings.ReminderEnabled ? "включены ✅" : "выключены ❌";
                    await _botClient.SendMessage(
                        chatId: chatId,
                        text: $"🔔 Напоминания {statusText}",
                        cancellationToken: ct);
                    _logger.LogInformation("User {UserId} toggled reminders to {Enabled}",
                        user.Id, settings.ReminderEnabled);
                }
                break;
            }

            case "settings_reminder_time":
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: "⏰ Для настройки времени напоминания используйте приложение → Настройки.\n\n" +
                          "Формат: HH:mm (например, 21:00)",
                    cancellationToken: ct);
                break;

            case "settings_week_end":
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: "📅 Для изменения конца недели используйте приложение → Настройки.\n\n" +
                          "⚠️ Смена конца недели создаёт переходный период.",
                    cancellationToken: ct);
                break;
        }
    }

    private static void CleanRecentCallbacks()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-10);
        foreach (var key in RecentCallbacks.Keys.ToArray())
        {
            if (RecentCallbacks.TryGetValue(key, out var ts) && ts < cutoff)
                RecentCallbacks.TryRemove(key, out _);
        }

        // Also clean old pending events
        foreach (var key in PendingEvents.Keys.ToArray())
        {
            if (PendingEvents.TryGetValue(key, out var pending) &&
                (DateTime.UtcNow - pending.CreatedAt).TotalMinutes > 5)
                PendingEvents.TryRemove(key, out _);
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
