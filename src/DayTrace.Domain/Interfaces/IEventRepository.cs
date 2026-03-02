using DayTrace.Domain.Entities;

namespace DayTrace.Domain.Interfaces;

public interface IEventRepository
{
    Task<Event> CreateAsync(Event evt, CancellationToken ct = default);
    Task<Event?> GetByIdAsync(Guid id, long userId, CancellationToken ct = default);

    /// <summary>
    /// Gets the non-deleted event for a user on a specific local date.
    /// Returns null if no event exists for that date.
    /// </summary>
    Task<Event?> GetByUserAndDateAsync(long userId, DateOnly localDate, CancellationToken ct = default);

    Task UpdateAsync(Event evt, CancellationToken ct = default);
    Task<(List<Event> Items, string? NextCursor)> ListAsync(
        long userId, DateOnly? from, DateOnly? to,
        int limit, string? cursor, CancellationToken ct = default);

    /// <summary>
    /// Gets all non-deleted events for a user within a date range [periodStart, periodEnd] inclusive.
    /// Used by summary generation.
    /// </summary>
    Task<List<Event>> GetByPeriodAsync(long userId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default);

    /// <summary>
    /// Counts non-deleted events for a user within a date range [periodStart, periodEnd] inclusive.
    /// </summary>
    Task<int> CountByPeriodAsync(long userId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default);

    /// <summary>Admin: count all non-deleted events for a user.</summary>
    Task<int> CountByUserAsync(long userId, CancellationToken ct = default);

    /// <summary>Admin: list events with filtering and pagination.</summary>
    Task<List<Event>> AdminListAsync(int limit, int offset, long? userId = null, DateOnly? from = null, DateOnly? to = null, int? importance = null, CancellationToken ct = default);

    /// <summary>Admin: count events with filtering.</summary>
    Task<int> AdminCountAsync(long? userId = null, DateOnly? from = null, DateOnly? to = null, int? importance = null, CancellationToken ct = default);

    /// <summary>Gets the earliest non-deleted event date for a user.</summary>
    Task<DateOnly?> GetFirstEventDateAsync(long userId);
}
