using DayTrace.Domain.Entities;

namespace DayTrace.Domain.Interfaces;

public interface IPeriodRunCounterRepository
{
    /// <summary>
    /// Gets or creates the run counter for a period. First access creates with last_run_number = 1.
    /// Uses INSERT ... ON CONFLICT DO NOTHING semantics.
    /// </summary>
    Task<PeriodRunCounter> GetOrCreateAsync(long userId, string periodType, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default);

    /// <summary>
    /// Atomically increments last_run_number and returns the updated counter.
    /// Used for force re-run mode.
    /// </summary>
    Task<PeriodRunCounter> IncrementRunNumberAsync(long userId, string periodType, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default);

    /// <summary>
    /// Conditionally increments run number only if the existing job is terminally failed
    /// (attempt_count >= 3, status = 'failed'). Returns updated counter or null if precondition not met.
    /// </summary>
    Task<PeriodRunCounter?> IncrementIfTerminalFailedAsync(long userId, string periodType, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default);
}
