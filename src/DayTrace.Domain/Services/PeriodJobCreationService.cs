using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;

namespace DayTrace.Domain.Services;

/// <summary>
/// Period job creation transaction (US-024, section 4.4.1).
/// Lock order: counters → events check → jobs → summaries.
/// Supports auto_trigger and force_rerun modes.
/// </summary>
public class PeriodJobCreationService
{
    private readonly PeriodRunCounterService _counterService;
    private readonly IPeriodJobRepository _jobRepo;
    private readonly ISummaryRepository _summaryRepo;
    private readonly IEventRepository _eventRepo;
    private readonly IDomainLogger _logger;

    public PeriodJobCreationService(
        PeriodRunCounterService counterService,
        IPeriodJobRepository jobRepo,
        ISummaryRepository summaryRepo,
        IEventRepository eventRepo,
        IDomainLogger logger)
    {
        _counterService = counterService;
        _jobRepo = jobRepo;
        _summaryRepo = summaryRepo;
        _eventRepo = eventRepo;
        _logger = logger;
    }

    public enum CreateMode
    {
        AutoTrigger,
        ForceRerun
    }

    public class CreateResult
    {
        public bool Success { get; set; }
        public string? Reason { get; set; } // "created", "already_generated", "empty_period", "idempotent_existing"
        public PeriodJob? Job { get; set; }
        public Summary? Summary { get; set; }
    }

    /// <summary>
    /// Creates a period job with proper locking and idempotency (section 4.4.1).
    /// </summary>
    public async Task<CreateResult> CreateAsync(
        long userId, string periodType, DateOnly periodStart, DateOnly periodEnd,
        CreateMode mode, CancellationToken ct = default)
    {
        _logger.LogPeriodJobStart("new", userId, periodType, periodStart, periodEnd);

        if (mode == CreateMode.AutoTrigger)
            return await CreateAutoTriggerAsync(userId, periodType, periodStart, periodEnd, ct);
        else
            return await CreateForceRerunAsync(userId, periodType, periodStart, periodEnd, ct);
    }

    private async Task<CreateResult> CreateAutoTriggerAsync(
        long userId, string periodType, DateOnly periodStart, DateOnly periodEnd,
        CancellationToken ct)
    {
        // Step 1: Get current run number (no increment)
        var (runNumber, idempotencyKey) = await _counterService.GetCurrentRunAsync(
            userId, periodType, periodStart, periodEnd, ct);

        // Step 2: Check event count — 0 events → no trigger
        var eventCount = await _eventRepo.CountByPeriodAsync(userId, periodStart, periodEnd, ct);
        if (eventCount == 0)
        {
            _logger.Info("PeriodJobCreation: auto_trigger skipped — 0 events for user={UserId}, period=[{Start}..{End}]",
                userId, periodStart.ToString("yyyy-MM-dd"), periodEnd.ToString("yyyy-MM-dd"));
            return new CreateResult { Success = false, Reason = "empty_period" };
        }

        // Step 3: Check if summary already exists with status=generated
        var existingSummary = await _summaryRepo.GetAsync(userId, periodType, periodStart, periodEnd, ct);
        if (existingSummary != null && existingSummary.Status == "generated")
        {
            _logger.Info("PeriodJobCreation: auto_trigger skipped — summary already generated for user={UserId}",
                userId);
            return new CreateResult { Success = false, Reason = "already_generated", Summary = existingSummary };
        }

        // Step 4: Terminal fail recovery
        var latestJob = await _jobRepo.GetLatestForPeriodAsync(userId, periodType, periodStart, periodEnd, ct);
        if (latestJob != null && latestJob.Status == "failed" && latestJob.AttemptCount >= 3)
        {
            var recovery = await _counterService.TryTerminalFailRecoveryAsync(
                userId, periodType, periodStart, periodEnd, ct);
            if (recovery != null)
            {
                runNumber = recovery.Value.RunNumber;
                idempotencyKey = recovery.Value.IdempotencyKey;

                // Mark old job as reconciled
                latestJob.ReconciledAt = DateTime.UtcNow;
                latestJob.RecoverySource = "auto_trigger";
                await _jobRepo.UpdateAsync(latestJob, ct);
            }
        }

        // Step 5: Create/get summary
        var summary = existingSummary;
        int targetVersion;
        if (summary == null)
        {
            summary = new Summary
            {
                UserId = userId,
                PeriodType = periodType,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                Status = "generating",
                Version = 1
            };
            summary = await _summaryRepo.CreateAsync(summary, ct);
            targetVersion = 1;
        }
        else
        {
            summary.Status = "generating";
            await _summaryRepo.UpdateAsync(summary, ct);
            targetVersion = summary.Version;
        }

        // Step 6: Create period job (ON CONFLICT returns existing)
        var job = new PeriodJob
        {
            IdempotencyKey = idempotencyKey,
            UserId = userId,
            PeriodType = periodType,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            RunNumber = runNumber,
            Status = "pending",
            AttemptCount = 0,
            TargetSummaryVersion = targetVersion,
            CreatedAt = DateTime.UtcNow
        };

        var (inserted, resultJob) = await _jobRepo.TryInsertAsync(job, ct);
        if (!inserted)
        {
            _logger.Info("PeriodJobCreation: idempotent hit for key={Key}", idempotencyKey);
            return new CreateResult
            {
                Success = true,
                Reason = "idempotent_existing",
                Job = resultJob,
                Summary = summary
            };
        }

        _logger.LogPeriodJobResult(resultJob.Id.ToString(), "created", eventCount);

        return new CreateResult
        {
            Success = true,
            Reason = "created",
            Job = resultJob,
            Summary = summary
        };
    }

    private async Task<CreateResult> CreateForceRerunAsync(
        long userId, string periodType, DateOnly periodStart, DateOnly periodEnd,
        CancellationToken ct)
    {
        // Step 1: Check existing summary
        var existingSummary = await _summaryRepo.GetAsync(userId, periodType, periodStart, periodEnd, ct);

        // Step 2: If no existing summary and 0 events → 400 empty_period
        var eventCount = await _eventRepo.CountByPeriodAsync(userId, periodStart, periodEnd, ct);
        if (existingSummary == null && eventCount == 0)
        {
            return new CreateResult { Success = false, Reason = "empty_period" };
        }

        // Step 3: Force increment run number
        var (runNumber, idempotencyKey) = await _counterService.ForceNewRunAsync(
            userId, periodType, periodStart, periodEnd, ct);

        // Step 4: Create/update summary
        Summary summary;
        int targetVersion;
        if (existingSummary != null)
        {
            existingSummary.Status = "generating";
            existingSummary.Version += 1;
            await _summaryRepo.UpdateAsync(existingSummary, ct);
            summary = existingSummary;
            targetVersion = existingSummary.Version;
        }
        else
        {
            summary = new Summary
            {
                UserId = userId,
                PeriodType = periodType,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                Status = "generating",
                Version = 1
            };
            summary = await _summaryRepo.CreateAsync(summary, ct);
            targetVersion = 1;
        }

        // Step 5: Create period job
        var job = new PeriodJob
        {
            IdempotencyKey = idempotencyKey,
            UserId = userId,
            PeriodType = periodType,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            RunNumber = runNumber,
            Status = "pending",
            AttemptCount = 0,
            TargetSummaryVersion = targetVersion,
            CreatedAt = DateTime.UtcNow
        };

        var (inserted, resultJob) = await _jobRepo.TryInsertAsync(job, ct);

        _logger.Info("PeriodJobCreation: force_rerun created job_id={JobId}, run_number={RunNumber}",
            resultJob.Id, runNumber);

        return new CreateResult
        {
            Success = true,
            Reason = inserted ? "created" : "idempotent_existing",
            Job = resultJob,
            Summary = summary
        };
    }
}
