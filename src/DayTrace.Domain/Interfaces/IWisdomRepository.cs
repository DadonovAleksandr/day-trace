using DayTrace.Domain.Entities;

namespace DayTrace.Domain.Interfaces;

public interface IWisdomRepository
{
    Task<Wisdom?> GetRandomAsync(CancellationToken ct = default);
    Task<int> CountAsync(CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<Wisdom> wisdoms, CancellationToken ct = default);
}
