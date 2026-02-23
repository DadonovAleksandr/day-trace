using DayTrace.Bot.Configuration;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

namespace DayTrace.Api.BackgroundServices;

/// <summary>
/// Background service that retries failed Telegram message deliveries
/// with exponential backoff, up to 5 attempts (US-040, NFR-1).
/// Runs every 30 seconds.
/// </summary>
public class DeliveryRetryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DeliveryRetryService> _logger;
    private readonly TelegramBotOptions _botOptions;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private const int MaxAttempts = 5;
    private const int MaxPerCycle = 20;

    public DeliveryRetryService(
        IServiceScopeFactory scopeFactory,
        IOptions<TelegramBotOptions> botOptions,
        ILogger<DeliveryRetryService> logger)
    {
        _scopeFactory = scopeFactory;
        _botOptions = botOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DeliveryRetryService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stoppingToken);
                await ProcessRetriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeliveryRetry: error during retry cycle");
            }
        }

        _logger.LogInformation("DeliveryRetryService stopped");
    }

    private async Task ProcessRetriesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var deliveryRepo = scope.ServiceProvider.GetRequiredService<IDeliveryAttemptRepository>();
        var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var botClient = scope.ServiceProvider.GetService<ITelegramBotClient>();

        if (botClient == null) return;

        var retryable = await deliveryRepo.GetRetryableAsync(MaxAttempts, MaxPerCycle, ct);
        if (retryable.Count == 0) return;

        _logger.LogInformation("DeliveryRetry: found {Count} retryable deliveries", retryable.Count);

        foreach (var attempt in retryable)
        {
            try
            {
                // Check backoff: exponential — 30s * 2^(attempt_number-1)
                var backoff = TimeSpan.FromSeconds(30 * Math.Pow(2, attempt.AttemptNumber - 1));
                var backoffReady = attempt.CreatedAt.Add(backoff);
                if (DateTime.UtcNow < backoffReady) continue;

                var user = await userRepo.GetByIdAsync(attempt.UserId, ct);
                if (user == null)
                {
                    attempt.Status = "terminal_failed";
                    attempt.ErrorMessage = "User not found";
                    await deliveryRepo.UpdateAsync(attempt, ct);
                    continue;
                }

                // Increment attempt
                attempt.AttemptNumber += 1;

                // Resolve period name for period-related deliveries
                var periodName = await ResolvePeriodNameAsync(attempt, scope, ct);

                // Build the message text based on delivery type
                var text = attempt.DeliveryType switch
                {
                    "reminder" => "📝 Не забудьте записать события дня! Откройте приложение или отправьте текст боту.",
                    "soft_reminder" => $"📋 Закончился период — вы можете сформировать итог {periodName} вручную через приложение.",
                    "summary_notification" => $"✅ Ваш итог {periodName} готов! Откройте приложение, чтобы посмотреть.",
                    _ => "📝 Напоминание от DayTrace"
                };

                var miniAppUrl = !string.IsNullOrEmpty(_botOptions.MiniAppUrl)
                    ? _botOptions.MiniAppUrl
                    : _botOptions.WebhookBaseUrl;

                var keyboard = new InlineKeyboardMarkup(new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithWebApp("📱 Открыть приложение", new Telegram.Bot.Types.WebAppInfo { Url = miniAppUrl }),
                    }
                });

                try
                {
                    var message = await botClient.SendMessage(
                        chatId: user.TelegramUserId,
                        text: text,
                        replyMarkup: keyboard,
                        cancellationToken: ct);

                    attempt.Status = "sent";
                    attempt.SentAt = DateTime.UtcNow;
                    attempt.TelegramMessageId = message.MessageId;
                    await deliveryRepo.UpdateAsync(attempt, ct);

                    _logger.LogInformation(
                        "DeliveryRetry: sent delivery_id={DeliveryId}, type={Type}, attempt={Attempt}",
                        attempt.Id, attempt.DeliveryType, attempt.AttemptNumber);
                }
                catch (Exception sendEx)
                {
                    var isTransient = IsTransientError(sendEx);

                    if (!isTransient || attempt.AttemptNumber >= MaxAttempts)
                    {
                        attempt.Status = "terminal_failed";
                    }
                    else
                    {
                        attempt.Status = "failed";
                    }

                    attempt.ErrorMessage = sendEx.Message.Length > 500 ? sendEx.Message[..500] : sendEx.Message;
                    await deliveryRepo.UpdateAsync(attempt, ct);

                    _logger.LogWarning(sendEx,
                        "DeliveryRetry: failed delivery_id={DeliveryId}, attempt={Attempt}, status={Status}",
                        attempt.Id, attempt.AttemptNumber, attempt.Status);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DeliveryRetry: error processing delivery_id={DeliveryId}", attempt.Id);
            }
        }
    }

    /// <summary>
    /// Resolves a human-readable period name for period-related deliveries.
    /// </summary>
    private static Task<string> ResolvePeriodNameAsync(
        DeliveryAttempt attempt, IServiceScope scope, CancellationToken ct)
    {
        if (attempt.DeliveryType is not ("soft_reminder" or "summary_notification"))
            return Task.FromResult("");

        return Task.FromResult("за период");
    }

    private static string GetPeriodDisplayName(string periodType) => periodType switch
    {
        "weekly" => "за неделю",
        "monthly" => "за месяц",
        "yearly" => "за год",
        _ => "за период"
    };

    private static bool IsTransientError(Exception ex)
    {
        if (ex is Telegram.Bot.Exceptions.ApiRequestException apiEx)
        {
            return apiEx.ErrorCode == 429 || apiEx.ErrorCode >= 500;
        }
        return ex is HttpRequestException or TaskCanceledException;
    }
}
