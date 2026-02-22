using DayTrace.Domain.Entities;

namespace DayTrace.Domain.Interfaces;

public interface IDayRatingRepository
{
    Task<DayRating?> GetAsync(long userId, DateOnly localDate, CancellationToken ct = default);
    Task<DayRating> CreateAsync(DayRating rating, CancellationToken ct = default);
    Task UpdateAsync(DayRating rating, CancellationToken ct = default);
}
