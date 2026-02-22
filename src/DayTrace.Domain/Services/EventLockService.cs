using DayTrace.Domain.Interfaces;

namespace DayTrace.Domain.Services;

/// <summary>
/// Service for checking event and summary lock status.
/// Events are locked when a higher-level summary (weekly) is already generated.
/// Summaries are locked when their parent summary (monthly/yearly) is already generated.
/// </summary>
public class EventLockService
{
    private readonly ISummaryRepository _summaryRepo;
    private readonly DateCalculationService _dateService;

    public EventLockService(ISummaryRepository summaryRepo, DateCalculationService dateService)
    {
        _summaryRepo = summaryRepo;
        _dateService = dateService;
    }

    /// <summary>
    /// Checks if an event is locked for editing/deletion.
    /// An event is locked when a weekly summary with status='generated' covers the event's date.
    /// </summary>
    public async Task<(bool Locked, string? LockedBy)> IsEventLockedAsync(
        long userId, DateOnly eventDate, CancellationToken ct = default)
    {
        // Compute week boundaries for the event date
        var (weekStart, weekEnd) = await _dateService.GetWeekBoundariesAsync(userId, eventDate, ct);

        // Check for a weekly summary with status 'generated'
        var summary = await _summaryRepo.GetAsync(userId, "weekly", weekStart, weekEnd, ct);
        if (summary != null && summary.Status == "generated")
        {
            return (true, "weekly");
        }

        return (false, null);
    }

    /// <summary>
    /// Checks if a summary regeneration is locked by a higher-level summary.
    /// Weekly is locked by monthly, monthly is locked by yearly.
    /// Yearly is the top level and is never locked.
    /// </summary>
    public async Task<(bool Locked, string? LockedBy)> IsSummaryLockedAsync(
        long userId, string periodType, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default)
    {
        switch (periodType.ToLowerInvariant())
        {
            case "weekly":
            {
                // Weekly is locked by monthly
                var (monthStart, monthEnd) = DateCalculationService.GetMonthBoundaries(periodStart);
                var summary = await _summaryRepo.GetAsync(userId, "monthly", monthStart, monthEnd, ct);
                if (summary != null && summary.Status == "generated")
                    return (true, "monthly");

                // Also check the month of period end (week may cross month boundary)
                if (periodEnd.Month != periodStart.Month || periodEnd.Year != periodStart.Year)
                {
                    var (monthStart2, monthEnd2) = DateCalculationService.GetMonthBoundaries(periodEnd);
                    var summary2 = await _summaryRepo.GetAsync(userId, "monthly", monthStart2, monthEnd2, ct);
                    if (summary2 != null && summary2.Status == "generated")
                        return (true, "monthly");
                }

                return (false, null);
            }
            case "monthly":
            {
                // Monthly is locked by yearly
                var (yearStart, yearEnd) = DateCalculationService.GetYearBoundaries(periodStart);
                var summary = await _summaryRepo.GetAsync(userId, "yearly", yearStart, yearEnd, ct);
                if (summary != null && summary.Status == "generated")
                    return (true, "yearly");
                return (false, null);
            }
            case "yearly":
                // Yearly is the top level, never locked
                return (false, null);
            default:
                return (false, null);
        }
    }
}
