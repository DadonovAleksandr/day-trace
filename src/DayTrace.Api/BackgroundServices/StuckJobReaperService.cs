using DayTrace.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Api.BackgroundServices;

/// <summary>
/// Combined background service for stuck job reaping, retry processing, and terminal
/// failure reconciliation (US-035, US-036, US-037, section 4.4.3).
/// Runs every 2 minutes.
/// </summary>
public class StuckJobReaperService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StuckJobReaperService> _logger;
    private static readonly TimeSpan CycleInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StuckTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ReconciliationCooldown = TimeSpan.FromMinutes(5);
    private const int MaxStuckPerCycle = 20;
    private const int MaxRetryPerCycle = 10;
    private const int MaxReconcilePerCycle = 10;
    private const int MaxRetryAttempts = 3;

    public StuckJobReaperService(
        IServiceScopeFactory scopeFactory,
        ILogger<StuckJobReaperService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StuckJobReaperService started (reap + retry + reconcile)");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CycleInterval, stoppingToken);

                using var scope = _scopeFactory.CreateScope();
                var jobRepo = scope.ServiceProvider.GetRequiredService<IPeriodJobRepository>();
                var summaryRepo = scope.ServiceProvider.GetRequiredService<ISummaryRepository>();
                var counterRepo = scope.ServiceProvider.GetRequiredService<IPeriodRunCounterRepository>();

                // Phase 1: Reap stuck jobs (US-035)
                await ReapStuckJobsAsync(jobRepo, summaryRepo, stoppingToken);

                // Phase 2: Retry failed jobs with backoff (US-036)
                var dbContext = scope.ServiceProvider.GetRequiredService<DayTrace.Infrastructure.Data.DayTraceDbContext>();
                await RetryFailedJobsAsync(jobRepo, dbContext, stoppingToken);

                // Phase 3: Reconcile terminally failed jobs (US-037)
                await ReconcileTerminalJobsAsync(jobRepo, summaryRepo, counterRepo, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StuckJobReaper: error during cycle");
            }
        }

        _logger.LogInformation("StuckJobReaperService stopped");
    }

    // ── Phase 1: Reap stuck jobs (US-035) ──────────────────────────────

    private async Task ReapStuckJobsAsync(
        IPeriodJobRepository jobRepo, ISummaryRepository summaryRepo, CancellationToken ct)
    {
        var stuckJobs = await jobRepo.GetStuckJobsAsync(StuckTimeout, MaxStuckPerCycle, ct);
        if (stuckJobs.Count == 0) return;

        _logger.LogInformation("StuckJobReaper: found {Count} stuck jobs", stuckJobs.Count);

        foreach (var job in stuckJobs)
        {
            try
            {
                // Mark job as failed; null lease_id = zombie worker protection
                job.Status = "failed";
                job.FinishedAt = DateTime.UtcNow;
                job.LeaseId = null;
                job.Error = "timeout: exceeded 5 min";
                await jobRepo.UpdateAsync(job, ct);

                // Update related summary to failed (fenced by target_summary_version)
                var summary = await summaryRepo.GetAsync(
                    job.UserId, job.PeriodType, job.PeriodStart, job.PeriodEnd, ct);

                if (summary != null && summary.Status == "generating" &&
                    summary.Version == job.TargetSummaryVersion)
                {
                    summary.Status = "failed";
                    await summaryRepo.UpdateAsync(summary, ct);

                    _logger.LogInformation(
                        "StuckJobReaper: marked summary as failed for job_id={JobId}, summary_id={SummaryId}",
                        job.Id, summary.Id);
                }

                _logger.LogWarning(
                    "StuckJobReaper: reaped stuck job_id={JobId}, user_id={UserId}, period=[{Start}..{End}], attempt={Attempt}",
                    job.Id, job.UserId,
                    job.PeriodStart.ToString("yyyy-MM-dd"),
                    job.PeriodEnd.ToString("yyyy-MM-dd"),
                    job.AttemptCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StuckJobReaper: failed to reap job_id={JobId}", job.Id);
            }
        }
    }

    // ── Phase 2: Retry failed jobs with exponential backoff (US-036) ───

    private async Task RetryFailedJobsAsync(
        IPeriodJobRepository jobRepo,
        DayTrace.Infrastructure.Data.DayTraceDbContext db,
        CancellationToken ct)
    {
        // Wrap in explicit transaction to hold FOR UPDATE SKIP LOCKED row-locks
        // Wrapped in ExecuteAsync to support NpgsqlRetryingExecutionStrategy
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            try
            {
                var retryable = await jobRepo.GetRetryableJobsAsync(MaxRetryAttempts, MaxRetryPerCycle, ct);
                if (retryable.Count == 0)
                {
                    await transaction.CommitAsync(ct);
                    return;
                }

                _logger.LogInformation("RetryProcessor: found {Count} retryable jobs", retryable.Count);

                foreach (var job in retryable)
                {
                    try
                    {
                        // Set status=retried, lease_id=NULL so worker picks it up
                        job.Status = "retried";
                        job.LeaseId = null;
                        await jobRepo.UpdateAsync(job, ct);

                        _logger.LogInformation(
                            "RetryProcessor: re-queued job_id={JobId}, attempt={Attempt}/{Max}",
                            job.Id, job.AttemptCount, MaxRetryAttempts);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "RetryProcessor: failed to retry job_id={JobId}", job.Id);
                    }
                }

                await transaction.CommitAsync(ct);
            }
            catch
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        });
    }

    // ── Phase 3: Terminal failure reconciliation (US-037) ──────────────

    private async Task ReconcileTerminalJobsAsync(
        IPeriodJobRepository jobRepo,
        ISummaryRepository summaryRepo,
        IPeriodRunCounterRepository counterRepo,
        CancellationToken ct)
    {
        // Terminal: status=failed, attempt_count >= 3, finished_at > 5 min ago, reconciled_at IS NULL
        var terminal = await jobRepo.GetTerminalFailedJobsAsync(ReconciliationCooldown, MaxReconcilePerCycle, ct);
        if (terminal.Count == 0) return;

        _logger.LogInformation("Reconciliation: found {Count} terminal-failed jobs", terminal.Count);

        foreach (var job in terminal)
        {
            try
            {
                // Precondition 1: no newer job with higher run_number
                var hasNewer = await jobRepo.HasNewerJobAsync(
                    job.UserId, job.PeriodType, job.PeriodStart, job.PeriodEnd, job.RunNumber, ct);
                if (hasNewer)
                {
                    // Only mark reconciled, no new job needed
                    job.ReconciledAt = DateTime.UtcNow;
                    await jobRepo.UpdateAsync(job, ct);
                    _logger.LogInformation(
                        "Reconciliation: newer job exists for job_id={JobId}, marking reconciled only", job.Id);
                    continue;
                }

                // Precondition 2: summary not already generated with version >= target
                var summary = await summaryRepo.GetAsync(
                    job.UserId, job.PeriodType, job.PeriodStart, job.PeriodEnd, ct);
                if (summary != null && summary.Status == "generated" &&
                    summary.Version >= job.TargetSummaryVersion)
                {
                    // Summary already good, just reconcile the job
                    job.ReconciledAt = DateTime.UtcNow;
                    await jobRepo.UpdateAsync(job, ct);
                    _logger.LogInformation(
                        "Reconciliation: summary already generated for job_id={JobId}, marking reconciled only", job.Id);
                    continue;
                }

                // Both preconditions pass: create recovery job
                // Increment run_number
                var updatedCounter = await counterRepo.IncrementRunNumberAsync(
                    job.UserId, job.PeriodType, job.PeriodStart, job.PeriodEnd, ct);
                var newRunNumber = updatedCounter.LastRunNumber;

                // Determine target summary version
                var targetVersion = 1;
                if (summary != null)
                {
                    summary.Status = "generating";
                    summary.Version += 1;
                    targetVersion = summary.Version;
                    await summaryRepo.UpdateAsync(summary, ct);
                }
                else
                {
                    // Create new summary
                    summary = new Domain.Entities.Summary
                    {
                        UserId = job.UserId,
                        PeriodType = job.PeriodType,
                        PeriodStart = job.PeriodStart,
                        PeriodEnd = job.PeriodEnd,
                        Status = "generating",
                        Version = 1
                    };
                    summary = await summaryRepo.CreateAsync(summary, ct);
                    targetVersion = summary.Version;
                }

                // Create new recovery job
                var idempotencyKey = Domain.Services.PeriodRunCounterService.ComputeIdempotencyKey(
                    job.UserId, job.PeriodType, job.PeriodStart, job.PeriodEnd, newRunNumber);

                var recoveryJob = new Domain.Entities.PeriodJob
                {
                    IdempotencyKey = idempotencyKey,
                    UserId = job.UserId,
                    PeriodType = job.PeriodType,
                    PeriodStart = job.PeriodStart,
                    PeriodEnd = job.PeriodEnd,
                    RunNumber = newRunNumber,
                    Status = "pending",
                    AttemptCount = 0,
                    TargetSummaryVersion = targetVersion,
                    CreatedAt = DateTime.UtcNow
                };

                await jobRepo.TryInsertAsync(recoveryJob, ct);

                // Mark old job reconciled
                job.ReconciledAt = DateTime.UtcNow;
                job.RecoverySource = "reconciliation";
                await jobRepo.UpdateAsync(job, ct);

                _logger.LogInformation(
                    "Reconciliation: created recovery job for job_id={JobId}, new_run_number={RunNumber}, recovery_job_id={RecoveryJobId}",
                    job.Id, newRunNumber, recoveryJob.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reconciliation: failed to reconcile job_id={JobId}", job.Id);
            }
        }
    }
}
