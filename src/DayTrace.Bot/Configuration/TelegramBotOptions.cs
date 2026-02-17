namespace DayTrace.Bot.Configuration;

public class TelegramBotOptions
{
    public const string SectionName = "TelegramBot";

    /// <summary>
    /// Telegram Bot API token. Must be set via environment variable or secrets.
    /// </summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// Secret token for webhook verification (X-Telegram-Bot-Api-Secret-Token).
    /// </summary>
    public string WebhookSecretToken { get; set; } = string.Empty;

    /// <summary>
    /// Base URL for the webhook (e.g., https://example.com).
    /// </summary>
    public string WebhookBaseUrl { get; set; } = string.Empty;
}
