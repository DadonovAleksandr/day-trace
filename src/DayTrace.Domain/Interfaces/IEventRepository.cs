using DayTrace.Domain.Entities;

namespace DayTrace.Domain.Interfaces;

public interface IEventRepository
{
    Task<Event> CreateAsync(Event evt, CancellationToken ct = default);
    Task<Event?> GetByIdAsync(Guid id, long userId, CancellationToken ct = default);
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
}
