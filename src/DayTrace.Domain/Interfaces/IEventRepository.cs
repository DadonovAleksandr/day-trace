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
}
