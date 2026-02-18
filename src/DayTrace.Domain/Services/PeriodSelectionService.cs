using DayTrace.Domain.Interfaces;

namespace DayTrace.Domain.Services;

/// <summary>
/// Deterministic period selection for manual summary runs (US-032, FR-5).
/// Selects the last fully completed period when no explicit dates are provided.
/// </summary>
public class PeriodSelectionService
{
    private readonly DateCalculationService _dateService;
    private readonly ISummaryRepository _summaryRepo;

    public PeriodSelectionService(
        DateCalculationService dateService,
        ISummaryRepository summaryRepo)
    {
        _dateService = dateService;
        _summaryRepo = summaryRepo;
    }

    public class PeriodSelection
    {
        public DateOnly PeriodStart { get; set; }
        public DateOnly PeriodEnd { get; set; }
    }

    /// <summary>
    /// Selects the target period for a manual run.
    /// Returns the last fully completed period of the given type.
    /// </summary>
    public async Task<PeriodSelection> SelectPeriodAsync(
        long userId, string periodType, string timezone, CancellationToken ct = default)
    {
        var todayLocal = _dateService.GetTodayLocal(timezone);

        return periodType switch
        {
            "weekly" => await SelectWeeklyPeriodAsync(userId, todayLocal, ct),
            "monthly" => SelectMonthlyPeriod(todayLocal),
            "yearly" => SelectYearlyPeriod(todayLocal),
            _ => throw new ArgumentException($"Invalid period type: {periodType}")
        };
    }

    /// <summary>
    /// Weekly: last fully completed weekly period.
    /// A period is "completed" if today_local > period_end,
    /// OR if today == period_end AND an auto-trigger already created a summary (status ∈ {generating, generated}).
    /// </summary>
    private async Task<PeriodSelection> SelectWeeklyPeriodAsync(
        long userId, DateOnly todayLocal, CancellationToken ct)
    {
        // Get the current week boundaries (the week that contains today)
        var (currentWeekStart, currentWeekEnd) = await _dateService.GetWeekBoundariesAsync(userId, todayLocal, ct);

        if (todayLocal == currentWeekEnd)
        {
            // Exception: check if auto-trigger already created a summary for this week
            var summary = await _summaryRepo.GetAsync(userId, "weekly", currentWeekStart, currentWeekEnd, ct);
            if (summary != null && (summary.Status == "generating" || summary.Status == "generated"))
            {
                // Current week is "completed" for manual run purposes
                return new PeriodSelection
                {
                    PeriodStart = currentWeekStart,
                    PeriodEnd = currentWeekEnd
                };
            }
        }

        if (todayLocal > currentWeekEnd)
        {
            // Today is past this week's end — current period is completed
            return new PeriodSelection
            {
                PeriodStart = currentWeekStart,
                PeriodEnd = currentWeekEnd
            };
        }

        // Not yet completed — get the previous week
        var previousDay = currentWeekStart.AddDays(-1);
        var (prevWeekStart, prevWeekEnd) = await _dateService.GetWeekBoundariesAsync(userId, previousDay, ct);

        return new PeriodSelection
        {
            PeriodStart = prevWeekStart,
            PeriodEnd = prevWeekEnd
        };
    }

    /// <summary>
    /// Monthly: last fully completed calendar month.
    /// </summary>
    private static PeriodSelection SelectMonthlyPeriod(DateOnly todayLocal)
    {
        // If today is the last day of the month, this month might be completed
        // But for monthly, "fully completed" means today > month_end,
        // i.e., we're in the next month. So we always select the previous month.
        // Exception for last day: same logic as weekly
        var (currentMonthStart, currentMonthEnd) = DateCalculationService.GetMonthBoundaries(todayLocal);

        if (todayLocal >= currentMonthEnd)
        {
            // Today is the last day of the month or past it — this month is completed
            return new PeriodSelection
            {
                PeriodStart = currentMonthStart,
                PeriodEnd = currentMonthEnd
            };
        }

        // Previous month
        var prevMonthDate = currentMonthStart.AddDays(-1);
        var (prevMonthStart, prevMonthEnd) = DateCalculationService.GetMonthBoundaries(prevMonthDate);

        return new PeriodSelection
        {
            PeriodStart = prevMonthStart,
            PeriodEnd = prevMonthEnd
        };
    }

    /// <summary>
    /// Yearly: last fully completed calendar year.
    /// </summary>
    private static PeriodSelection SelectYearlyPeriod(DateOnly todayLocal)
    {
        var (currentYearStart, currentYearEnd) = DateCalculationService.GetYearBoundaries(todayLocal);

        if (todayLocal >= currentYearEnd)
        {
            // Today is Dec 31 or past — this year is completed
            return new PeriodSelection
            {
                PeriodStart = currentYearStart,
                PeriodEnd = currentYearEnd
            };
        }

        // Previous year
        var prevYearDate = currentYearStart.AddDays(-1);
        var (prevYearStart, prevYearEnd) = DateCalculationService.GetYearBoundaries(prevYearDate);

        return new PeriodSelection
        {
            PeriodStart = prevYearStart,
            PeriodEnd = prevYearEnd
        };
    }
}
