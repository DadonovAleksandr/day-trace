using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;

namespace DayTrace.Domain.Services;

/// <summary>
/// Centralized service for all timezone-aware date calculations.
/// Handles today_local, period boundaries (week/month/year), and DST.
/// Per FR-2, FR-2.1, FR-4.4.
/// </summary>
public class DateCalculationService
{
    private readonly IWeekScheduleHistoryRepository _weekScheduleRepo;

    public DateCalculationService(IWeekScheduleHistoryRepository weekScheduleRepo)
    {
        _weekScheduleRepo = weekScheduleRepo;
    }

    /// <summary>
    /// Computes today's local date for a given IANA timezone.
    /// </summary>
    public DateOnly GetTodayLocal(string timezone)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        return DateOnly.FromDateTime(now);
    }

    /// <summary>
    /// Converts a UTC DateTime to local DateTime in the given timezone.
    /// </summary>
    public DateTime ConvertToLocal(DateTime utcDateTime, string timezone)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, tz);
    }

    /// <summary>
    /// Parses and validates an IANA timezone string.
    /// Returns null if invalid.
    /// </summary>
    public static TimeZoneInfo? TryGetTimeZone(string timezone)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }

    /// <summary>
    /// Validates an IANA timezone string.
    /// </summary>
    public static bool IsValidTimezone(string timezone)
    {
        return TryGetTimeZone(timezone) != null;
    }

    // ========== Week Boundaries ==========

    /// <summary>
    /// Computes the week start and end dates for a given local date,
    /// using the user's week_schedule_history to determine week_end day.
    /// All period boundaries are inclusive DATE ranges (FR-2.1).
    /// </summary>
    public async Task<(DateOnly WeekStart, DateOnly WeekEnd)> GetWeekBoundariesAsync(
        long userId, DateOnly localDate, CancellationToken ct = default)
    {
        var weekEndDay = await GetEffectiveWeekEndDayAsync(userId, localDate, ct);
        return ComputeWeekBoundaries(localDate, weekEndDay);
    }

    /// <summary>
    /// Computes week boundaries from a date and a known week-end day.
    /// </summary>
    public static (DateOnly WeekStart, DateOnly WeekEnd) ComputeWeekBoundaries(DateOnly localDate, DayOfWeek weekEndDay)
    {
        // week_end is the last day of the week
        // week_start = week_end - 6 days (7-day week)
        var weekEnd = localDate;

        // Move forward to the next week_end day (or today if it's already week_end)
        while (weekEnd.DayOfWeek != weekEndDay)
        {
            weekEnd = weekEnd.AddDays(1);
        }

        var weekStart = weekEnd.AddDays(-6);
        return (weekStart, weekEnd);
    }

    /// <summary>
    /// Gets the effective week_end day for a given local date from schedule history.
    /// Applies fallback rule: if localDate < min(effective_from), use earliest record (FR-4.4).
    /// </summary>
    public async Task<DayOfWeek> GetEffectiveWeekEndDayAsync(
        long userId, DateOnly localDate, CancellationToken ct = default)
    {
        // Try to get the effective schedule for this date
        var schedule = await _weekScheduleRepo.GetEffectiveForDateAsync(userId, localDate, ct);

        if (schedule == null)
        {
            // Fallback: use earliest record (FR-4.4)
            schedule = await _weekScheduleRepo.GetEarliestAsync(userId, ct);
        }

        if (schedule == null)
        {
            // No schedule at all — default to Sunday
            return DayOfWeek.Sunday;
        }

        return ParseDayOfWeek(schedule.WeekEnd);
    }

    // ========== Month Boundaries ==========

    /// <summary>
    /// Computes month boundaries (first day, last day) for a given local date.
    /// All boundaries are inclusive (FR-2.1).
    /// </summary>
    public static (DateOnly MonthStart, DateOnly MonthEnd) GetMonthBoundaries(DateOnly localDate)
    {
        var monthStart = new DateOnly(localDate.Year, localDate.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        return (monthStart, monthEnd);
    }

    /// <summary>
    /// Returns true if the given local date is the last day of its month.
    /// </summary>
    public static bool IsLastDayOfMonth(DateOnly localDate)
    {
        var (_, monthEnd) = GetMonthBoundaries(localDate);
        return localDate == monthEnd;
    }

    // ========== Year Boundaries ==========

    /// <summary>
    /// Computes year boundaries (Jan 1, Dec 31) for a given local date.
    /// All boundaries are inclusive (FR-2.1).
    /// </summary>
    public static (DateOnly YearStart, DateOnly YearEnd) GetYearBoundaries(DateOnly localDate)
    {
        var yearStart = new DateOnly(localDate.Year, 1, 1);
        var yearEnd = new DateOnly(localDate.Year, 12, 31);
        return (yearStart, yearEnd);
    }

    /// <summary>
    /// Returns true if the given local date is December 31.
    /// </summary>
    public static bool IsDecember31(DateOnly localDate)
    {
        return localDate.Month == 12 && localDate.Day == 31;
    }

    // ========== Helpers ==========

    /// <summary>
    /// Parses a day name string (e.g., "Sunday") to DayOfWeek.
    /// </summary>
    public static DayOfWeek ParseDayOfWeek(string dayName)
    {
        return dayName.ToLowerInvariant() switch
        {
            "sunday" => DayOfWeek.Sunday,
            "monday" => DayOfWeek.Monday,
            "tuesday" => DayOfWeek.Tuesday,
            "wednesday" => DayOfWeek.Wednesday,
            "thursday" => DayOfWeek.Thursday,
            "friday" => DayOfWeek.Friday,
            "saturday" => DayOfWeek.Saturday,
            _ => throw new ArgumentException($"Invalid day of week: {dayName}")
        };
    }

    /// <summary>
    /// Gets the backdate window: [today - 30, today] in user's timezone.
    /// Per FR-1, events can be backdated up to 30 days.
    /// </summary>
    public (DateOnly MinDate, DateOnly MaxDate) GetBackdateWindow(string timezone)
    {
        var today = GetTodayLocal(timezone);
        return (today.AddDays(-30), today);
    }
}
