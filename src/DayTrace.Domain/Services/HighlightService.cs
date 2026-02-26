using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;

namespace DayTrace.Domain.Services;

/// <summary>
/// Service for setting the highlight (main) event of a period summary.
/// Replaces the old auto-generation mechanism with manual user selection.
/// </summary>
public class HighlightService
{
    private readonly ISummaryRepository _summaryRepo;
    private readonly IEventRepository _eventRepo;
    private readonly EventLockService _lockService;

    private static readonly HashSet<string> ValidPeriodTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "weekly", "monthly", "yearly"
    };

    public HighlightService(
        ISummaryRepository summaryRepo,
        IEventRepository eventRepo,
        EventLockService lockService)
    {
        _summaryRepo = summaryRepo;
        _eventRepo = eventRepo;
        _lockService = lockService;
    }

    public record SetHighlightResult(
        bool Success,
        Summary? Summary = null,
        string? Error = null,
        string? Message = null,
        string? LockedBy = null);

    /// <summary>
    /// Sets the highlight event for a period summary.
    /// Creates the summary if it doesn't exist, or updates the existing one.
    /// </summary>
    public async Task<SetHighlightResult> SetHighlightAsync(
        long userId,
        Guid eventId,
        string periodType,
        DateOnly periodStart,
        DateOnly periodEnd,
        CancellationToken ct = default)
    {
        var normalizedPeriodType = periodType.ToLowerInvariant();

        // 1. Validate periodType
        if (!ValidPeriodTypes.Contains(normalizedPeriodType))
            return new SetHighlightResult(false, Error: "invalid_period",
                Message: $"periodType must be one of: weekly, monthly, yearly. Got: {periodType}");

        if (periodStart > periodEnd)
            return new SetHighlightResult(false, Error: "invalid_period",
                Message: "period_start must be <= period_end");

        // 2. Load event and verify ownership
        var evt = await _eventRepo.GetByIdAsync(eventId, userId, ct);
        if (evt == null)
            return new SetHighlightResult(false, Error: "event_not_found",
                Message: "Событие не найдено");

        // 3. Verify event falls within the period
        if (evt.LocalDate < periodStart || evt.LocalDate > periodEnd)
            return new SetHighlightResult(false, Error: "event_outside_period",
                Message: $"Событие ({evt.LocalDate:yyyy-MM-dd}) не входит в период [{periodStart:yyyy-MM-dd}..{periodEnd:yyyy-MM-dd}]");

        // 4. Check lock by higher-level summary
        var (locked, lockedBy) = await _lockService.IsSummaryLockedAsync(
            userId, normalizedPeriodType, periodStart, periodEnd, ct);
        if (locked)
            return new SetHighlightResult(false, Error: "locked_by_summary",
                Message: "Изменение заблокировано вышестоящим итогом", LockedBy: lockedBy);

        // 5. Find or create summary
        var summary = await _summaryRepo.GetAsync(userId, normalizedPeriodType, periodStart, periodEnd, ct);
        if (summary == null)
        {
            summary = new Summary
            {
                UserId = userId,
                PeriodType = normalizedPeriodType,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                Status = "generated",
                Version = 1,
                HighlightEventId = eventId,
                LastGeneratedAt = DateTime.UtcNow
            };
            summary = await _summaryRepo.CreateAsync(summary, ct);
        }
        else
        {
            summary.HighlightEventId = eventId;
            summary.Status = "generated";
            summary.LastGeneratedAt = DateTime.UtcNow;
            summary.Version += 1;
            await _summaryRepo.UpdateAsync(summary, ct);
        }

        return new SetHighlightResult(true, Summary: summary);
    }
}
