using DayTrace.Domain.Entities;

namespace DayTrace.Domain.Interfaces;

public interface ISubscriptionRepository
{
    Task<Subscription?> GetByUserIdAsync(long userId);
    Task<Subscription> CreateAsync(Subscription subscription);
    Task<Subscription> UpdateAsync(Subscription subscription);
    Task<(List<Subscription> Items, int Total)> GetAllAsync(int limit, int offset, string? statusFilter = null);
}
