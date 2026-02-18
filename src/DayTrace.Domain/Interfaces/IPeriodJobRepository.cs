using DayTrace.Domain.Entities;

namespace DayTrace.Domain.Interfaces;

public interface IPeriodJobRepository
{
    /// <summary>
    /// Tries to insert a period job. Returns (true, job) if inserted, (false, existing) if duplicate by idempotency_key.
    /// </summary>
    Task<(bool Inserted, PeriodJob Job)> TryInsertAsync(PeriodJob job, CancellationToken ct = default);

    /// <summary>
    /// Gets a period job by idempotency key.
    /// </summary>
    Task<PeriodJob?> GetByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Gets the latest job for a period (by run_number DESC).
    /// </summary>
    Task<PeriodJob?> GetLatestForPeriodAsync(long userId, string periodType, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default);

    /// <summary>
    /// Updates a period job.
    /// </summary>
    Task UpdateAsync(PeriodJob job, CancellationToken ct = default);

    /// <summary>
    /// Claims pending/retried jobs for processing (SELECT FOR UPDATE SKIP LOCKED).
    /// </summary>
    Task<List<PeriodJob>> ClaimPendingJobsAsync(int maxJobs, CancellationToken ct = default);

    /// <summary>
    /// Gets stuck running jobs (running longer than timeout).
    /// </summary>
    Task<List<PeriodJob>> GetStuckJobsAsync(TimeSpan timeout, int maxJobs, CancellationToken ct = default);

    /// <summary>
    /// Gets failed jobs eligible for retry (attempt_count < maxAttempts, backoff elapsed).
    /// </summary>
    Task<List<PeriodJob>> GetRetryableJobsAsync(int maxAttempts, int maxJobs, CancellationToken ct = default);

    /// <summary>
    /// Gets terminally failed jobs eligible for reconciliation.
    /// </summary>
    Task<List<PeriodJob>> GetTerminalFailedJobsAsync(TimeSpan cooldown, int maxJobs, CancellationToken ct = default);

    /// <summary>
    /// Checks if a newer job exists for a period with a higher run_number.
    /// </summary>
    Task<bool> HasNewerJobAsync(long userId, string periodType, DateOnly periodStart, DateOnly periodEnd, int runNumber, CancellationToken ct = default);

    /// <summary>Admin: list period jobs with filtering and pagination.</summary>
    Task<List<PeriodJob>> AdminListAsync(int limit, int offset, string? status = null, long? userId = null, CancellationToken ct = default);

    /// <summary>Admin: count period jobs with filtering.</summary>
    Task<int> AdminCountAsync(string? status = null, long? userId = null, CancellationToken ct = default);
}
