using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class PeriodJobRepository : IPeriodJobRepository
{
    private readonly DayTraceDbContext _context;

    public PeriodJobRepository(DayTraceDbContext context)
    {
        _context = context;
    }

    public async Task<(bool Inserted, PeriodJob Job)> TryInsertAsync(PeriodJob job, CancellationToken ct = default)
    {
        var existing = await GetByIdempotencyKeyAsync(job.IdempotencyKey, ct);
        if (existing != null)
            return (false, existing);

        try
        {
            _context.PeriodJobs.Add(job);
            await _context.SaveChangesAsync(ct);
            return (true, job);
        }
        catch (DbUpdateException)
        {
            _context.Entry(job).State = EntityState.Detached;
            var winner = await GetByIdempotencyKeyAsync(job.IdempotencyKey, ct);
            return (false, winner ?? job);
        }
    }

    public async Task<PeriodJob?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default)
    {
        return await _context.PeriodJobs
            .FirstOrDefaultAsync(j => j.IdempotencyKey == idempotencyKey, ct);
    }

    public async Task<PeriodJob?> GetLatestForPeriodAsync(
        long userId, string periodType, DateOnly periodStart, DateOnly periodEnd,
        CancellationToken ct = default)
    {
        return await _context.PeriodJobs
            .Where(j =>
                j.UserId == userId &&
                j.PeriodType == periodType &&
                j.PeriodStart == periodStart &&
                j.PeriodEnd == periodEnd)
            .OrderByDescending(j => j.RunNumber)
            .FirstOrDefaultAsync(ct);
    }

    public async Task UpdateAsync(PeriodJob job, CancellationToken ct = default)
    {
        _context.PeriodJobs.Update(job);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<List<PeriodJob>> ClaimPendingJobsAsync(int maxJobs, CancellationToken ct = default)
    {
        // SELECT FOR UPDATE SKIP LOCKED via raw SQL
        return await _context.PeriodJobs
            .FromSqlInterpolated(
                $@"SELECT * FROM period_jobs 
                   WHERE status IN ('pending', 'retried')
                   ORDER BY created_at ASC
                   LIMIT {maxJobs}
                   FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);
    }

    public async Task<List<PeriodJob>> GetStuckJobsAsync(TimeSpan timeout, int maxJobs, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - timeout;
        return await _context.PeriodJobs
            .Where(j => j.Status == "running" && j.StartedAt < cutoff)
            .OrderBy(j => j.StartedAt)
            .Take(maxJobs)
            .ToListAsync(ct);
    }

    public async Task<List<PeriodJob>> GetRetryableJobsAsync(int maxAttempts, int maxJobs, CancellationToken ct = default)
    {
        // Backoff: 30s * 2^(attempt_count-1) → 30s after 1st fail, 60s after 2nd
        // finished_at + interval must be in the past
        return await _context.PeriodJobs
            .FromSqlInterpolated(
                $@"SELECT * FROM period_jobs
                   WHERE status = 'failed'
                     AND attempt_count < {maxAttempts}
                     AND finished_at IS NOT NULL
                     AND finished_at + (INTERVAL '30 seconds' * POWER(2, attempt_count - 1)) < NOW()
                   ORDER BY finished_at ASC
                   LIMIT {maxJobs}
                   FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);
    }

    public async Task<List<PeriodJob>> GetTerminalFailedJobsAsync(TimeSpan cooldown, int maxJobs, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - cooldown;
        return await _context.PeriodJobs
            .Where(j =>
                j.Status == "failed" &&
                j.AttemptCount >= 3 &&
                j.FinishedAt < cutoff &&
                j.ReconciledAt == null)
            .OrderBy(j => j.FinishedAt)
            .Take(maxJobs)
            .ToListAsync(ct);
    }

    public async Task<bool> HasNewerJobAsync(
        long userId, string periodType, DateOnly periodStart, DateOnly periodEnd,
        int runNumber, CancellationToken ct = default)
    {
        return await _context.PeriodJobs
            .AnyAsync(j =>
                j.UserId == userId &&
                j.PeriodType == periodType &&
                j.PeriodStart == periodStart &&
                j.PeriodEnd == periodEnd &&
                j.RunNumber > runNumber, ct);
    }

    public async Task<List<PeriodJob>> AdminListAsync(int limit, int offset, string? status = null, long? userId = null, CancellationToken ct = default)
    {
        var query = _context.PeriodJobs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(j => j.Status == status);
        if (userId.HasValue)
            query = query.Where(j => j.UserId == userId.Value);

        return await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<int> AdminCountAsync(string? status = null, long? userId = null, CancellationToken ct = default)
    {
        var query = _context.PeriodJobs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(j => j.Status == status);
        if (userId.HasValue)
            query = query.Where(j => j.UserId == userId.Value);

        return await query.CountAsync(ct);
    }
}
