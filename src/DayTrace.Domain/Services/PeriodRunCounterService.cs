using System.Security.Cryptography;
using System.Text;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;

namespace DayTrace.Domain.Services;

/// <summary>
/// Manages period run counters and idempotency key generation for period jobs (US-023, FR-8.1).
/// </summary>
public class PeriodRunCounterService
{
    private readonly IPeriodRunCounterRepository _counterRepo;
    private readonly IDomainLogger _logger;

    public PeriodRunCounterService(
        IPeriodRunCounterRepository counterRepo,
        IDomainLogger logger)
    {
        _counterRepo = counterRepo;
        _logger = logger;
    }

    /// <summary>
    /// Generates a deterministic idempotency key = hash(user_id, period_type, period_start, period_end, run_number).
    /// </summary>
    public static string ComputeIdempotencyKey(long userId, string periodType, DateOnly periodStart, DateOnly periodEnd, int runNumber)
    {
        var input = $"{userId}|{periodType}|{periodStart:yyyy-MM-dd}|{periodEnd:yyyy-MM-dd}|{runNumber}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>
    /// Gets the current run number for a period (idempotent trigger mode).
    /// First access: creates counter with last_run_number = 1.
    /// Does NOT increment — used by auto-triggers.
    /// </summary>
    public async Task<(int RunNumber, string IdempotencyKey)> GetCurrentRunAsync(
        long userId, string periodType, DateOnly periodStart, DateOnly periodEnd,
        CancellationToken ct = default)
    {
        var counter = await _counterRepo.GetOrCreateAsync(userId, periodType, periodStart, periodEnd, ct);
        var key = ComputeIdempotencyKey(userId, periodType, periodStart, periodEnd, counter.LastRunNumber);

        _logger.Info("PeriodRunCounter: idempotent trigger for user={UserId}, period={PeriodType} [{Start}..{End}], run_number={RunNumber}",
            userId, periodType, periodStart.ToString("yyyy-MM-dd"), periodEnd.ToString("yyyy-MM-dd"), counter.LastRunNumber);

        return (counter.LastRunNumber, key);
    }

    /// <summary>
    /// Increments run number for a force re-run. Returns new run number + idempotency key.
    /// </summary>
    public async Task<(int RunNumber, string IdempotencyKey)> ForceNewRunAsync(
        long userId, string periodType, DateOnly periodStart, DateOnly periodEnd,
        CancellationToken ct = default)
    {
        var counter = await _counterRepo.IncrementRunNumberAsync(userId, periodType, periodStart, periodEnd, ct);
        var key = ComputeIdempotencyKey(userId, periodType, periodStart, periodEnd, counter.LastRunNumber);

        _logger.Info("PeriodRunCounter: force re-run for user={UserId}, period={PeriodType} [{Start}..{End}], new run_number={RunNumber}",
            userId, periodType, periodStart.ToString("yyyy-MM-dd"), periodEnd.ToString("yyyy-MM-dd"), counter.LastRunNumber);

        return (counter.LastRunNumber, key);
    }

    /// <summary>
    /// Attempts terminal fail recovery: conditionally increments run_number if existing job is terminally failed.
    /// Returns null if no terminal failure found.
    /// </summary>
    public async Task<(int RunNumber, string IdempotencyKey)?> TryTerminalFailRecoveryAsync(
        long userId, string periodType, DateOnly periodStart, DateOnly periodEnd,
        CancellationToken ct = default)
    {
        var counter = await _counterRepo.IncrementIfTerminalFailedAsync(userId, periodType, periodStart, periodEnd, ct);
        if (counter == null)
            return null;

        var key = ComputeIdempotencyKey(userId, periodType, periodStart, periodEnd, counter.LastRunNumber);

        _logger.Info("PeriodRunCounter: terminal fail recovery for user={UserId}, period={PeriodType} [{Start}..{End}], new run_number={RunNumber}",
            userId, periodType, periodStart.ToString("yyyy-MM-dd"), periodEnd.ToString("yyyy-MM-dd"), counter.LastRunNumber);

        return (counter.LastRunNumber, key);
    }
}
