using DayTrace.Domain.Services;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace DayTrace.Bot.Handlers;

/// <summary>
/// Dispatches incoming Telegram updates to appropriate handlers (US-042).
/// </summary>
public class BotUpdateHandler
{
    private readonly ITelegramBotClient _botClient;
    private readonly UserRegistrationService _registrationService;
    private readonly ILogger<BotUpdateHandler> _logger;

    public BotUpdateHandler(
        ITelegramBotClient botClient,
        UserRegistrationService registrationService,
        ILogger<BotUpdateHandler> logger)
    {
        _botClient = botClient;
        _registrationService = registrationService;
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

        if (isNew)
        {
            // Welcome message with timezone hint
            var welcomeText = user.Settings?.Timezone == "UTC"
                ? "👋 Добро пожаловать в DayTrace!\n\n" +
                  "Записывайте события дня, а я помогу сформировать итоги за неделю, месяц и год.\n\n" +
                  "💡 Откройте Mini App, чтобы мы определили ваш часовой пояс."
                : "👋 Добро пожаловать в DayTrace!\n\n" +
                  "Записывайте события дня, а я помогу сформировать итоги за неделю, месяц и год.";

            await _botClient.SendMessage(
                chatId: message.Chat.Id,
                text: welcomeText,
                replyMarkup: GetQuickActionKeyboard(),
                cancellationToken: ct);
        }
        else
        {
            await _botClient.SendMessage(
                chatId: message.Chat.Id,
                text: "С возвращением! 👋 Чем могу помочь?",
                replyMarkup: GetQuickActionKeyboard(),
                cancellationToken: ct);
        }
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
        // Ensure user exists
        var telegramUserId = message.From!.Id;
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
    /// Unrecognized text → help message.
    /// </summary>
    private async Task HandleUnrecognizedTextAsync(Message message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message.Text)) return;

        await _botClient.SendMessage(
            chatId: message.Chat.Id,
            text: "Я пока не могу обработать этот текст. Используйте /help для справки или кнопки ниже:",
            replyMarkup: GetQuickActionKeyboard(),
            cancellationToken: ct);
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, CancellationToken ct)
    {
        _logger.LogInformation("Received callback query from user {UserId}: {Data}",
            callbackQuery.From.Id, callbackQuery.Data);

        // Acknowledge the callback to stop "loading" spinner
        await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);

        // Route callback by data prefix (US-043, US-044, US-045 will handle specific cases)
        var data = callbackQuery.Data ?? string.Empty;

        if (data.StartsWith("settings_"))
        {
            await HandleSettingsCallbackAsync(callbackQuery, data, ct);
        }
        else
        {
            _logger.LogDebug("Unhandled callback data: {Data}", data);
        }
    }

    /// <summary>
    /// Handle settings-related callbacks (US-045).
    /// </summary>
    private async Task HandleSettingsCallbackAsync(CallbackQuery query, string data, CancellationToken ct)
    {
        var chatId = query.Message?.Chat.Id ?? query.From.Id;

        switch (data)
        {
            case "settings_timezone":
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: "🕐 Для автоматического определения часового пояса откройте Mini App.\n\n" +
                          "Или укажите вручную (например, Europe/Moscow, America/New_York) через Mini App → Настройки.",
                    cancellationToken: ct);
                break;

            case "settings_toggle_reminder":
                // Will be fully implemented in US-045 with actual settings update
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: "Для переключения напоминаний используйте Mini App → Настройки.",
                    cancellationToken: ct);
                break;

            case "settings_reminder_time":
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: "⏰ Для настройки времени напоминания используйте Mini App → Настройки.",
                    cancellationToken: ct);
                break;

            case "settings_week_end":
                await _botClient.SendMessage(
                    chatId: chatId,
                    text: "📅 Для изменения конца недели используйте Mini App → Настройки.",
                    cancellationToken: ct);
                break;
        }
    }

    private Task HandleUnknownUpdateAsync(Update update, CancellationToken ct)
    {
        _logger.LogDebug("Received unknown update type {UpdateType}", update.Type);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Quick action inline keyboard: [Add Event] [Weekly] [Monthly] [Yearly] [Mini App] [Settings].
    /// </summary>
    private static InlineKeyboardMarkup GetQuickActionKeyboard()
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📝 Добавить событие", "add_event"),
                InlineKeyboardButton.WithCallbackData("⚙️ Настройки", "show_settings"),
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("📅 Неделя", "summary_weekly"),
                InlineKeyboardButton.WithCallbackData("📆 Месяц", "summary_monthly"),
                InlineKeyboardButton.WithCallbackData("📊 Год", "summary_yearly"),
            },
            new[]
            {
                InlineKeyboardButton.WithWebApp("📱 Mini App", new WebAppInfo { Url = "https://daytrace.app" }),
            }
        });
    }
}
