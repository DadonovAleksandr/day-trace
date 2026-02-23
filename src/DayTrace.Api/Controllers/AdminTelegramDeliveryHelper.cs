using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using Telegram.Bot;

namespace DayTrace.Api.Controllers;

internal sealed record AdminTelegramDeliveryResult(
    DeliveryAttempt Attempt,
    bool IsSuccess,
    int? TelegramMessageId,
    string? ErrorMessage);

internal static class AdminTelegramDeliveryHelper
{
    public static async Task<AdminTelegramDeliveryResult> SendAndLogAsync(
        IDeliveryAttemptRepository deliveryAttemptRepo,
        ITelegramBotClient botClient,
        long userId,
        long telegramUserId,
        string deliveryType,
        long? referenceId,
        string text,
        CancellationToken ct)
    {
        var attempt = new DeliveryAttempt
        {
            UserId = userId,
            DeliveryType = deliveryType,
            ReferenceId = referenceId,
            AttemptNumber = 1,
            Status = "pending",
            ScheduledAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        await deliveryAttemptRepo.CreateAsync(attempt, ct);

        if (telegramUserId <= 0)
        {
            attempt.Status = "terminal_failed";
            attempt.ErrorMessage = "User has no TelegramUserId";
            await deliveryAttemptRepo.UpdateAsync(attempt, ct);
            return new AdminTelegramDeliveryResult(attempt, false, null, attempt.ErrorMessage);
        }

        try
        {
            var message = await botClient.SendMessage(
                chatId: telegramUserId,
                text: text,
                cancellationToken: ct);

            attempt.Status = "sent";
            attempt.SentAt = DateTime.UtcNow;
            attempt.TelegramMessageId = message.MessageId;
            await deliveryAttemptRepo.UpdateAsync(attempt, ct);

            return new AdminTelegramDeliveryResult(attempt, true, message.MessageId, null);
        }
        catch (Exception ex)
        {
            // deliveryType is not handled by DeliveryRetryService, so never leave "failed"
            attempt.Status = "terminal_failed";
            attempt.ErrorMessage = TrimError(ex.Message);
            await deliveryAttemptRepo.UpdateAsync(attempt, ct);

            return new AdminTelegramDeliveryResult(attempt, false, null, attempt.ErrorMessage);
        }
    }

    private static string TrimError(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Unknown error";

        return message.Length > 500 ? message[..500] : message;
    }
}
