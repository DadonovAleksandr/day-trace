using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;

namespace DayTrace.Domain.Services;

/// <summary>
/// Handles auto-triggering of summary generation on event creation (US-027, US-028, US-029).
/// Checks weekly/monthly/yearly boundary conditions and creates period_jobs via PeriodJobCreationService.
/// </summary>
public class AutoTriggerService
{
    private readonly DateCalculationService _dateService;
    private readonly PeriodJobCreationService _periodJobService;
    private readonly IPromptDeliveryRepository _promptDeliveryRepo;
    private readonly IUserSettingsRepository _settingsRepo;
    private readonly IDomainLogger _logger;

    public AutoTriggerService(
        DateCalculationService dateService,
        PeriodJobCreationService periodJobService,
        IPromptDeliveryRepository promptDeliveryRepo,
        IUserSettingsRepository settingsRepo,
        IDomainLogger logger)
    {
        _dateService = dateService;
        _periodJobService = periodJobService;
        _promptDeliveryRepo = promptDeliveryRepo;
        _settingsRepo = settingsRepo;
        _logger = logger;
    }

    /// <summary>
    /// Called after a successful event creation. Checks all auto-trigger conditions
    /// for weekly, monthly, and yearly periods. Creates period_jobs as needed.
    /// Event and period_job are in separate transactions per FR-8.2a.
    /// </summary>
    public async Task CheckAndTriggerAsync(Event savedEvent, CancellationToken ct = default)
    {
        var settings = await _settingsRepo.GetByUserIdAsync(savedEvent.UserId, ct);
        if (settings == null)
        {
            _logger.Info("AutoTrigger: no settings found for user_id={UserId}, skipping", savedEvent.UserId);
            return;
        }

        var todayLocal = _dateService.GetTodayLocal(settings.Timezone);

        // US-027: Weekly auto-trigger — only when today is the week_end day
        await TryWeeklyTriggerAsync(savedEvent, settings, todayLocal, ct);

        // US-028: Monthly auto-trigger — only when today is the last day of the month
        await TryMonthlyTriggerAsync(savedEvent, settings, todayLocal, ct);

        // US-029: Yearly auto-trigger — only when today is December 31
        await TryYearlyTriggerAsync(savedEvent, settings, todayLocal, ct);
    }

    /// <summary>
    /// US-027: Weekly auto-trigger on event creation.
    /// Condition: today_local == week_end day AND event's local_date is within the target week.
    /// </summary>
    private async Task TryWeeklyTriggerAsync(
        Event savedEvent, UserSettings settings, DateOnly todayLocal, CancellationToken ct)
    {
        var weekEndDay = DateCalculationService.ParseDayOfWeek(settings.WeekEnd);

        // Only trigger on the week_end day
        if (todayLocal.DayOfWeek != weekEndDay)
            return;

        // Compute the target week boundaries
        var (weekStart, weekEnd) = await _dateService.GetWeekBoundariesAsync(
            savedEvent.UserId, todayLocal, ct);

        // Event's local_date must fall within the target week (backdated events outside → no trigger)
        if (savedEvent.LocalDate < weekStart || savedEvent.LocalDate > weekEnd)
        {
            _logger.Info(
                "AutoTrigger: weekly skipped — event local_date={EventDate} outside target week [{Start}..{End}]",
                savedEvent.LocalDate.ToString("yyyy-MM-dd"),
                weekStart.ToString("yyyy-MM-dd"),
                weekEnd.ToString("yyyy-MM-dd"));
            return;
        }

        await CreatePeriodJobAndRecordDeliveryAsync(
            savedEvent.UserId, "weekly", weekStart, weekEnd, ct);
    }

    /// <summary>
    /// US-028: Monthly auto-trigger on event creation.
    /// Condition: today_local is the last day of the month AND event's local_date is within the month.
    /// </summary>
    private async Task TryMonthlyTriggerAsync(
        Event savedEvent, UserSettings settings, DateOnly todayLocal, CancellationToken ct)
    {
        // Only trigger on the last day of the month
        if (!DateCalculationService.IsLastDayOfMonth(todayLocal))
            return;

        var (monthStart, monthEnd) = DateCalculationService.GetMonthBoundaries(todayLocal);

        // Event's local_date must fall within the current month
        if (savedEvent.LocalDate < monthStart || savedEvent.LocalDate > monthEnd)
        {
            _logger.Info(
                "AutoTrigger: monthly skipped — event local_date={EventDate} outside target month [{Start}..{End}]",
                savedEvent.LocalDate.ToString("yyyy-MM-dd"),
                monthStart.ToString("yyyy-MM-dd"),
                monthEnd.ToString("yyyy-MM-dd"));
            return;
        }

        await CreatePeriodJobAndRecordDeliveryAsync(
            savedEvent.UserId, "monthly", monthStart, monthEnd, ct);
    }

    /// <summary>
    /// US-029: Yearly auto-trigger on event creation.
    /// Condition: today_local is December 31 AND event's local_date is within the current year.
    /// </summary>
    private async Task TryYearlyTriggerAsync(
        Event savedEvent, UserSettings settings, DateOnly todayLocal, CancellationToken ct)
    {
        // Only trigger on December 31
        if (!DateCalculationService.IsDecember31(todayLocal))
            return;

        var (yearStart, yearEnd) = DateCalculationService.GetYearBoundaries(todayLocal);

        // Event's local_date must fall within the current year
        if (savedEvent.LocalDate < yearStart || savedEvent.LocalDate > yearEnd)
        {
            _logger.Info(
                "AutoTrigger: yearly skipped — event local_date={EventDate} outside target year [{Start}..{End}]",
                savedEvent.LocalDate.ToString("yyyy-MM-dd"),
                yearStart.ToString("yyyy-MM-dd"),
                yearEnd.ToString("yyyy-MM-dd"));
            return;
        }

        await CreatePeriodJobAndRecordDeliveryAsync(
            savedEvent.UserId, "yearly", yearStart, yearEnd, ct);
    }

    /// <summary>
    /// Creates a period_job via auto_trigger mode and records a prompt_delivery with channel=auto.
    /// Handles idempotency (PeriodJobCreationService handles duplicate jobs).
    /// </summary>
    private async Task CreatePeriodJobAndRecordDeliveryAsync(
        long userId, string periodType, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct)
    {
        try
        {
            var result = await _periodJobService.CreateAsync(
                userId, periodType, periodStart, periodEnd,
                PeriodJobCreationService.CreateMode.AutoTrigger, ct);

            _logger.Info(
                "AutoTrigger: {PeriodType} for user_id={UserId} [{Start}..{End}] — success={Success}, reason={Reason}",
                periodType, userId,
                periodStart.ToString("yyyy-MM-dd"),
                periodEnd.ToString("yyyy-MM-dd"),
                result.Success, result.Reason);

            // Record prompt_delivery with channel=auto (idempotent by prompt_id)
            if (result.Success)
            {
                var promptId = $"auto_{periodType}_{userId}_{periodStart:yyyy-MM-dd}_{periodEnd:yyyy-MM-dd}";

                var existingDelivery = await _promptDeliveryRepo.GetByPromptIdAsync(promptId, ct);
                if (existingDelivery == null)
                {
                    var delivery = new PromptDelivery
                    {
                        PromptId = promptId,
                        UserId = userId,
                        PeriodType = periodType,
                        PeriodStart = periodStart,
                        PeriodEnd = periodEnd,
                        SentAt = DateTime.UtcNow,
                        Channel = "auto",
                        Status = "sent"
                    };
                    await _promptDeliveryRepo.CreateAsync(delivery, ct);
                }
            }
        }
        catch (Exception ex)
        {
            // Auto-trigger failures should not break event creation
            _logger.Error(
                "AutoTrigger: {PeriodType} failed for user_id={UserId} [{Start}..{End}]: {Error}",
                periodType, userId,
                periodStart.ToString("yyyy-MM-dd"),
                periodEnd.ToString("yyyy-MM-dd"),
                ex.Message);
        }
    }
}
