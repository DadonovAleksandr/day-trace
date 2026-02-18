using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Domain.Services;

namespace DayTrace.Api.BackgroundServices;

/// <summary>
/// Background worker that claims pending/retried period jobs, generates summary content,
/// and finalizes with proper fencing (US-025, section 4.4.2).
/// </summary>
public class PeriodJobWorkerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PeriodJobWorkerService> _logger;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int MaxJobsPerCycle = 5;

    public PeriodJobWorkerService(
        IServiceScopeFactory scopeFactory,
        ILogger<PeriodJobWorkerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PeriodJobWorkerService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollInterval, stoppingToken);
                await ProcessJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PeriodJobWorker: error during processing cycle");
            }
        }

        _logger.LogInformation("PeriodJobWorkerService stopped");
    }

    private async Task ProcessJobsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IPeriodJobRepository>();
        var summaryRepo = scope.ServiceProvider.GetRequiredService<ISummaryRepository>();
        var genService = scope.ServiceProvider.GetRequiredService<SummaryGenerationService>();
        var dbContext = scope.ServiceProvider.GetRequiredService<DayTrace.Infrastructure.Data.DayTraceDbContext>();

        // Claim pending/retried jobs within an explicit transaction
        // (SELECT FOR UPDATE SKIP LOCKED requires a wrapping transaction to hold the lock)
        List<PeriodJob> jobs;
        await using (var transaction = await dbContext.Database.BeginTransactionAsync(ct))
        {
            jobs = await jobRepo.ClaimPendingJobsAsync(MaxJobsPerCycle, ct);

            // Mark jobs as "running" while still holding the lock
            foreach (var job in jobs)
            {
                var leaseId = Guid.NewGuid();
                job.Status = "running";
                job.LeaseId = leaseId;
                job.AttemptCount += 1;
                job.StartedAt = DateTime.UtcNow;
                await jobRepo.UpdateAsync(job, ct);
            }

            await transaction.CommitAsync(ct);
        }

        // Process claimed jobs outside the transaction
        foreach (var job in jobs)
        {
            try
            {
                await ProcessSingleJobAsync(job, jobRepo, summaryRepo, genService, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PeriodJobWorker: failed to process job_id={JobId}", job.Id);
                await MarkJobFailedAsync(job, jobRepo, summaryRepo, ex.Message, ct);
            }
        }
    }

    private async Task ProcessSingleJobAsync(
        PeriodJob job,
        IPeriodJobRepository jobRepo,
        ISummaryRepository summaryRepo,
        SummaryGenerationService genService,
        CancellationToken ct)
    {
        _logger.LogInformation("PeriodJobWorker: processing job_id={JobId}, run_number={RunNumber}",
            job.Id, job.RunNumber);

        // Job already claimed (status=running, leaseId set) in ProcessJobsAsync transaction
        var leaseId = job.LeaseId!.Value;

        // Partial success recovery: check if summary already generated for target version
        var summary = await summaryRepo.GetAsync(
            job.UserId, job.PeriodType, job.PeriodStart, job.PeriodEnd, ct);

        if (summary != null && summary.Status == "generated" && summary.Version >= job.TargetSummaryVersion)
        {
            _logger.LogInformation("PeriodJobWorker: summary already generated for job_id={JobId}, marking success", job.Id);
            job.Status = "success";
            job.FinishedAt = DateTime.UtcNow;
            await jobRepo.UpdateAsync(job, ct);
            return;
        }

        // Summary status recovery: if status=failed → set to generating
        if (summary != null && summary.Status == "failed")
        {
            summary.Status = "generating";
            await summaryRepo.UpdateAsync(summary, ct);
        }

        // Work phase: generate summary content
        var (contentJson, sourceEventIds) = await genService.GenerateAsync(
            job.UserId, job.PeriodStart, job.PeriodEnd, ct);

        // TX2: Finalize — fenced update
        if (summary != null)
        {
            var rowsAffected = await summaryRepo.FencedUpdateAsync(
                summary.Id, job.TargetSummaryVersion, leaseId,
                "generated", contentJson, sourceEventIds, ct);

            if (rowsAffected == 0)
            {
                // Fencing failed — job was superseded
                _logger.LogWarning("PeriodJobWorker: fenced update returned 0 rows for job_id={JobId}, marking superseded", job.Id);
                job.Status = "superseded";
                job.FinishedAt = DateTime.UtcNow;
                await jobRepo.UpdateAsync(job, ct);
                return;
            }
        }

        // Verify lease is still ours
        var currentJob = await jobRepo.GetByIdempotencyKeyAsync(job.IdempotencyKey, ct);
        if (currentJob == null || currentJob.LeaseId != leaseId)
        {
            _logger.LogWarning("PeriodJobWorker: lease lost for job_id={JobId}, marking superseded", job.Id);
            job.Status = "superseded";
            job.FinishedAt = DateTime.UtcNow;
            await jobRepo.UpdateAsync(job, ct);
            return;
        }

        // Mark job as success
        job.Status = "success";
        job.FinishedAt = DateTime.UtcNow;
        await jobRepo.UpdateAsync(job, ct);

        _logger.LogInformation("PeriodJobWorker: job_id={JobId} completed successfully, events={EventCount}",
            job.Id, sourceEventIds.Length);
    }

    private async Task MarkJobFailedAsync(
        PeriodJob job,
        IPeriodJobRepository jobRepo,
        ISummaryRepository summaryRepo,
        string error,
        CancellationToken ct)
    {
        try
        {
            job.Status = "failed";
            job.FinishedAt = DateTime.UtcNow;
            job.Error = error.Length > 1000 ? error[..1000] : error;
            await jobRepo.UpdateAsync(job, ct);

            // Mark summary as failed only if version matches (fenced fail)
            // Prevents a stale/old job from corrupting a newer summary version
            var summary = await summaryRepo.GetAsync(
                job.UserId, job.PeriodType, job.PeriodStart, job.PeriodEnd, ct);
            if (summary != null && summary.Status == "generating"
                && summary.Version == job.TargetSummaryVersion)
            {
                summary.Status = "failed";
                await summaryRepo.UpdateAsync(summary, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PeriodJobWorker: failed to mark job as failed, job_id={JobId}", job.Id);
        }
    }
}
