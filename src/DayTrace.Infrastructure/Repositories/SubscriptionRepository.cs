using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class SubscriptionRepository : ISubscriptionRepository
{
    private readonly DayTraceDbContext _context;

    public SubscriptionRepository(DayTraceDbContext context)
    {
        _context = context;
    }

    public async Task<Subscription?> GetByUserIdAsync(long userId)
    {
        return await _context.Subscriptions
            .FirstOrDefaultAsync(s => s.UserId == userId);
    }

    public async Task<Subscription?> GetByUserIdWithUserAsync(long userId)
    {
        return await _context.Subscriptions
            .Include(s => s.User)
            .FirstOrDefaultAsync(s => s.UserId == userId);
    }

    public async Task<Subscription> CreateAsync(Subscription subscription)
    {
        _context.Subscriptions.Add(subscription);
        await _context.SaveChangesAsync();
        return subscription;
    }

    public async Task<Subscription> UpdateAsync(Subscription subscription)
    {
        _context.Subscriptions.Update(subscription);
        await _context.SaveChangesAsync();
        return subscription;
    }

    public async Task<(List<Subscription> Items, int Total)> GetAllAsync(int limit, int offset, string? statusFilter = null)
    {
        var query = _context.Subscriptions
            .Include(s => s.User)
            .AsQueryable();

        if (!string.IsNullOrEmpty(statusFilter))
        {
            var now = DateTime.UtcNow;
            var graceBoundary = now.AddDays(-7);
            var inactiveBaseQuery = query.Where(s => !s.IsExempt
                && (s.SubscriptionExpiresAt == null || s.SubscriptionExpiresAt <= now)
                && (s.TrialExpiresAt == null || s.TrialExpiresAt <= now)
                && !(s.TrialStartedAt == null && s.SubscriptionExpiresAt == null));

            query = statusFilter.ToLowerInvariant() switch
            {
                "exempt" => query.Where(s => s.IsExempt),
                "trial" => query.Where(s => !s.IsExempt && s.TrialExpiresAt != null && s.TrialExpiresAt > now),
                "active" => query.Where(s => !s.IsExempt && s.SubscriptionExpiresAt != null && s.SubscriptionExpiresAt > now),
                "grace_period" => inactiveBaseQuery.Where(s =>
                    (s.SubscriptionExpiresAt != null
                        && (s.TrialExpiresAt == null || s.SubscriptionExpiresAt >= s.TrialExpiresAt)
                        && s.SubscriptionExpiresAt > graceBoundary)
                    || (s.TrialExpiresAt != null
                        && (s.SubscriptionExpiresAt == null || s.TrialExpiresAt > s.SubscriptionExpiresAt)
                        && s.TrialExpiresAt > graceBoundary)),
                "expired" => inactiveBaseQuery.Where(s =>
                    (s.SubscriptionExpiresAt != null
                        && (s.TrialExpiresAt == null || s.SubscriptionExpiresAt >= s.TrialExpiresAt)
                        && s.SubscriptionExpiresAt <= graceBoundary)
                    || (s.TrialExpiresAt != null
                        && (s.SubscriptionExpiresAt == null || s.TrialExpiresAt > s.SubscriptionExpiresAt)
                        && s.TrialExpiresAt <= graceBoundary)
                    || (s.TrialStartedAt != null && s.TrialExpiresAt == null && s.SubscriptionExpiresAt == null)),
                "not_started" => query.Where(s => !s.IsExempt
                    && s.TrialStartedAt == null
                    && s.SubscriptionExpiresAt == null),
                _ => query
            };
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(s => s.UpdatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();

        return (items, total);
    }
}
