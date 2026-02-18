using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class DeliveryAttemptRepository : IDeliveryAttemptRepository
{
    private readonly DayTraceDbContext _context;

    public DeliveryAttemptRepository(DayTraceDbContext context)
    {
        _context = context;
    }

    public async Task<DeliveryAttempt> CreateAsync(DeliveryAttempt attempt, CancellationToken ct = default)
    {
        _context.DeliveryAttempts.Add(attempt);
        await _context.SaveChangesAsync(ct);
        return attempt;
    }

    public async Task UpdateAsync(DeliveryAttempt attempt, CancellationToken ct = default)
    {
        _context.DeliveryAttempts.Update(attempt);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<bool> HasReminderForDateAsync(
        long userId, DateTime scheduledAtUtcStart, DateTime scheduledAtUtcEnd,
        CancellationToken ct = default)
    {
        return await _context.DeliveryAttempts
            .AnyAsync(d =>
                d.UserId == userId &&
                d.DeliveryType == "reminder" &&
                d.ScheduledAt >= scheduledAtUtcStart &&
                d.ScheduledAt < scheduledAtUtcEnd &&
                d.Status != "terminal_failed", ct);
    }

    public async Task<List<DeliveryAttempt>> GetRetryableAsync(
        int maxAttempts, int maxItems, CancellationToken ct = default)
    {
        return await _context.DeliveryAttempts
            .Where(d =>
                d.Status == "failed" &&
                d.AttemptNumber < maxAttempts)
            .OrderBy(d => d.CreatedAt)
            .Take(maxItems)
            .ToListAsync(ct);
    }
}
