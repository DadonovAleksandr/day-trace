using DayTrace.Domain.Entities;

namespace DayTrace.Domain.Interfaces;

public interface IWeekScheduleHistoryRepository
{
    /// <summary>
    /// Gets all week schedule history records for a user, ordered by EffectiveFromLocalDate ascending.
    /// </summary>
    Task<List<WeekScheduleHistory>> GetByUserIdAsync(long userId, CancellationToken ct = default);

    /// <summary>
    /// Gets the effective week schedule for a given local date.
    /// Returns the record with the largest effective_from_local_date that is <= the given date.
    /// </summary>
    Task<WeekScheduleHistory?> GetEffectiveForDateAsync(long userId, DateOnly localDate, CancellationToken ct = default);

    /// <summary>
    /// Gets the earliest (first) schedule record for a user (fallback rule per FR-4.4).
    /// </summary>
    Task<WeekScheduleHistory?> GetEarliestAsync(long userId, CancellationToken ct = default);

    Task<WeekScheduleHistory> CreateAsync(WeekScheduleHistory record, CancellationToken ct = default);
}
