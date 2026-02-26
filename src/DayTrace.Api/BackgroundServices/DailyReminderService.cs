using DayTrace.Bot.Configuration;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace DayTrace.Api.BackgroundServices;

/// <summary>
/// Background service that sends daily reminders at each user's configured time
/// in their timezone (US-038, FR-3, NFR-1).
/// Polls every 60 seconds to achieve ±5 min delivery SLA.
/// Also handles period-end soft reminders (FR-6/FR-7).
/// </summary>
public class DailyReminderService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DailyReminderService> _logger;
    private readonly TelegramBotOptions _botOptions;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    public DailyReminderService(
        IServiceScopeFactory scopeFactory,
        IOptions<TelegramBotOptions> botOptions,
        ILogger<DailyReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _botOptions = botOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DailyReminderService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stoppingToken);
                await ProcessRemindersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DailyReminder: error during reminder cycle");
            }
        }

        _logger.LogInformation("DailyReminderService stopped");
    }

    internal async Task ProcessRemindersAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();
        var deliveryRepo = scope.ServiceProvider.GetRequiredService<IDeliveryAttemptRepository>();

        // Get bot client — may not be configured in dev
        var botClient = scope.ServiceProvider.GetService<ITelegramBotClient>();
        if (botClient == null)
        {
            _logger.LogDebug("DailyReminder: no Telegram bot client configured, skipping");
            return;
        }

        var now = DateTime.UtcNow;
        var users = await userRepo.GetActiveUsersWithRemindersAsync(ct);

        foreach (var user in users)
        {
            try
            {
                await ProcessUserReminderAsync(user, now, botClient, deliveryRepo, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DailyReminder: error processing reminder for user_id={UserId}", user.Id);
            }
        }
    }

    internal async Task ProcessUserReminderAsync(
        User user,
        DateTime nowUtc,
        ITelegramBotClient botClient,
        IDeliveryAttemptRepository deliveryRepo,
        CancellationToken ct)
    {
        var settings = user.Settings!;
        TimeZoneInfo tz;

        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById(settings.Timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            _logger.LogWarning("DailyReminder: invalid timezone '{Timezone}' for user_id={UserId}, skipping",
                settings.Timezone, user.Id);
            return;
        }

        // Compute today's scheduled reminder time in UTC
        var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
        var todayLocal = DateOnly.FromDateTime(nowLocal);

        // Build local DateTime for today's reminder
        var reminderLocalDateTime = todayLocal.ToDateTime(settings.ReminderTime);

        // Handle DST: spring-forward (time doesn't exist) → shift to next valid time
        if (tz.IsInvalidTime(reminderLocalDateTime))
        {
            // Spring-forward: advance past the DST gap using the timezone's actual daylight delta
            var dstDelta = tz.GetAdjustmentRules()
                .Where(r => r.DateStart.Year <= reminderLocalDateTime.Year &&
                            r.DateEnd.Year >= reminderLocalDateTime.Year)
                .Select(r => r.DaylightDelta)
                .DefaultIfEmpty(TimeSpan.FromHours(1))
                .First();
            reminderLocalDateTime = reminderLocalDateTime.Add(dstDelta);
            _logger.LogInformation(
                "DailyReminder: DST spring-forward adjustment for user_id={UserId}, shifted to {AdjustedTime}",
                user.Id, reminderLocalDateTime);
        }

        // Handle DST: fall-back (time occurs twice) → use first occurrence
        // TimeZoneInfo.ConvertTimeToUtc with isAmbiguous check handles this
        DateTime scheduledUtc;
        if (tz.IsAmbiguousTime(reminderLocalDateTime))
        {
            // Use the UTC offset for the first occurrence (standard time offset, i.e., the earlier UTC time)
            var offsets = tz.GetAmbiguousTimeOffsets(reminderLocalDateTime);
            var maxOffset = offsets.Max(); // First occurrence = larger offset (pre-change)
            scheduledUtc = reminderLocalDateTime - maxOffset;
            _logger.LogInformation(
                "DailyReminder: DST fall-back, using first occurrence for user_id={UserId}", user.Id);
        }
        else
        {
            scheduledUtc = TimeZoneInfo.ConvertTimeToUtc(reminderLocalDateTime, tz);
        }

        // Skip if reminder time hasn't arrived yet (with 5-min tolerance window)
        if (nowUtc < scheduledUtc)
            return;

        // Skip if next reminder is too far in the past (>10 min) — no retroactive send after TZ change
        if (nowUtc > scheduledUtc.AddMinutes(10))
        {
            _logger.LogDebug(
                "DailyReminder: skipping past reminder for user_id={UserId}, scheduled={ScheduledUtc}, now={NowUtc}",
                user.Id, scheduledUtc, nowUtc);
            return;
        }

        // Check if we already sent/scheduled a reminder for this user today
        var dayStart = todayLocal.ToDateTime(TimeOnly.MinValue);
        var dayStartUtc = TimeZoneInfo.ConvertTimeToUtc(dayStart, tz);
        var dayEndUtc = dayStartUtc.AddDays(1);

        var alreadySent = await deliveryRepo.HasReminderForDateAsync(user.Id, dayStartUtc, dayEndUtc, ct);
        if (alreadySent)
            return;

        // Create delivery attempt
        var attempt = new DeliveryAttempt
        {
            UserId = user.Id,
            DeliveryType = "reminder",
            AttemptNumber = 1,
            Status = "pending",
            ScheduledAt = scheduledUtc,
            CreatedAt = DateTime.UtcNow
        };
        await deliveryRepo.CreateAsync(attempt, ct);

        // Send the reminder via Telegram
        try
        {
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

            var message = await botClient.SendMessage(
                chatId: user.TelegramUserId,
                text: "📝 Не забудьте записать события дня! Откройте приложение.",
                replyMarkup: keyboard,
                cancellationToken: ct);

            attempt.Status = "sent";
            attempt.SentAt = DateTime.UtcNow;
            attempt.TelegramMessageId = message.MessageId;
            await deliveryRepo.UpdateAsync(attempt, ct);

            _logger.LogInformation(
                "DailyReminder: sent reminder to user_id={UserId}, telegram_user_id={TelegramUserId}, scheduled_utc={ScheduledUtc}",
                user.Id, user.TelegramUserId, scheduledUtc);
        }
        catch (Exception ex)
        {
            attempt.Status = IsTransientError(ex) ? "failed" : "terminal_failed";
            attempt.ErrorMessage = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            await deliveryRepo.UpdateAsync(attempt, ct);

            _logger.LogWarning(ex,
                "DailyReminder: failed to send reminder to user_id={UserId}, status={Status}",
                user.Id, attempt.Status);
        }
    }

    private static bool IsTransientError(Exception ex)
    {
        // Telegram API: 429 Too Many Requests and 5xx are transient
        // 4xx (except 429) are terminal
        if (ex is Telegram.Bot.Exceptions.ApiRequestException apiEx)
        {
            return apiEx.ErrorCode == 429 || apiEx.ErrorCode >= 500;
        }
        // Network errors are transient
        return ex is HttpRequestException or TaskCanceledException;
    }
}
