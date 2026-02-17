using DayTrace.Domain.Entities;

namespace DayTrace.Domain.Interfaces;

public interface ITimezoneHistoryRepository
{
    Task<TimezoneHistory> CreateAsync(TimezoneHistory record, CancellationToken ct = default);
    Task<TimezoneHistory?> GetLatestAsync(long userId, CancellationToken ct = default);
}
