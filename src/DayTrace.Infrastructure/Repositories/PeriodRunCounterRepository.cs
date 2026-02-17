using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class PeriodRunCounterRepository : IPeriodRunCounterRepository
{
    private readonly DayTraceDbContext _context;

    public PeriodRunCounterRepository(DayTraceDbContext context)
    {
        _context = context;
    }

    public async Task<PeriodRunCounter> GetOrCreateAsync(
        long userId, string periodType, DateOnly periodStart, DateOnly periodEnd,
        CancellationToken ct = default)
    {
        // Try to find existing
        var existing = await _context.PeriodRunCounters
            .FirstOrDefaultAsync(c =>
                c.UserId == userId &&
                c.PeriodType == periodType &&
                c.PeriodStart == periodStart &&
                c.PeriodEnd == periodEnd, ct);

        if (existing != null)
            return existing;

        // INSERT ... ON CONFLICT DO NOTHING via try/catch on unique constraint
        var counter = new PeriodRunCounter
        {
            UserId = userId,
            PeriodType = periodType,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            LastRunNumber = 1
        };

        try
        {
            _context.PeriodRunCounters.Add(counter);
            await _context.SaveChangesAsync(ct);
            return counter;
        }
        catch (DbUpdateException)
        {
            // Concurrent insert — detach and re-query
            _context.Entry(counter).State = EntityState.Detached;
            return await _context.PeriodRunCounters
                .FirstAsync(c =>
                    c.UserId == userId &&
                    c.PeriodType == periodType &&
                    c.PeriodStart == periodStart &&
                    c.PeriodEnd == periodEnd, ct);
        }
    }

    public async Task<PeriodRunCounter> IncrementRunNumberAsync(
        long userId, string periodType, DateOnly periodStart, DateOnly periodEnd,
        CancellationToken ct = default)
    {
        // Ensure counter exists first
        var counter = await GetOrCreateAsync(userId, periodType, periodStart, periodEnd, ct);

        // Atomic increment via raw SQL for safety
        var rows = await _context.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE period_run_counters 
               SET last_run_number = last_run_number + 1 
               WHERE user_id = {userId} 
                 AND period_type = {periodType} 
                 AND period_start = {periodStart} 
                 AND period_end = {periodEnd}", ct);

        // Re-query to get updated value
        _context.Entry(counter).State = EntityState.Detached;
        return await _context.PeriodRunCounters
            .FirstAsync(c =>
                c.UserId == userId &&
                c.PeriodType == periodType &&
                c.PeriodStart == periodStart &&
                c.PeriodEnd == periodEnd, ct);
    }

    public async Task<PeriodRunCounter?> IncrementIfTerminalFailedAsync(
        long userId, string periodType, DateOnly periodStart, DateOnly periodEnd,
        CancellationToken ct = default)
    {
        // Check if there's a terminally failed job (status=failed, attempt_count >= 3)
        var terminalJob = await _context.PeriodJobs
            .Where(j =>
                j.UserId == userId &&
                j.PeriodType == periodType &&
                j.PeriodStart == periodStart &&
                j.PeriodEnd == periodEnd &&
                j.Status == "failed" &&
                j.AttemptCount >= 3)
            .OrderByDescending(j => j.RunNumber)
            .FirstOrDefaultAsync(ct);

        if (terminalJob == null)
            return null;

        // Increment atomically
        return await IncrementRunNumberAsync(userId, periodType, periodStart, periodEnd, ct);
    }
}
