using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using Telegram.Bot;

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
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private const int MaxAttempts = 5;
    private const int MaxPerCycle = 20;

    public DeliveryRetryService(
        IServiceScopeFactory scopeFactory,
        ILogger<DeliveryRetryService> logger)
    {
        _scopeFactory = scopeFactory;
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

                // Build the message text based on delivery type
                var text = attempt.DeliveryType switch
                {
                    "reminder" => "📝 Не забудьте записать события дня! Откройте Mini App или отправьте текст боту.",
                    "soft_reminder" => "📋 Вчера закончился период — вы можете сформировать итог вручную через Mini App.",
                    "summary_notification" => "✅ Ваш итог за период готов! Откройте Mini App, чтобы посмотреть.",
                    _ => "📝 Напоминание от DayTrace"
                };

                try
                {
                    var message = await botClient.SendMessage(
                        chatId: user.TelegramUserId,
                        text: text,
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

    private static bool IsTransientError(Exception ex)
    {
        if (ex is Telegram.Bot.Exceptions.ApiRequestException apiEx)
        {
            return apiEx.ErrorCode == 429 || apiEx.ErrorCode >= 500;
        }
        return ex is HttpRequestException or TaskCanceledException;
    }
}
