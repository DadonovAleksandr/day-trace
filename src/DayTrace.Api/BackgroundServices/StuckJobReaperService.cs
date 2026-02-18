using DayTrace.Domain.Interfaces;

namespace DayTrace.Api.BackgroundServices;

/// <summary>
/// Background reaper that detects stuck jobs (running > 5 min) and marks them failed
/// with proper lease_id fencing (US-035, section 4.4.3).
/// Runs every 2 minutes.
/// </summary>
public class StuckJobReaperService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StuckJobReaperService> _logger;
    private static readonly TimeSpan ReapInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StuckTimeout = TimeSpan.FromMinutes(5);
    private const int MaxJobsPerCycle = 20;

    public StuckJobReaperService(
        IServiceScopeFactory scopeFactory,
        ILogger<StuckJobReaperService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StuckJobReaperService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(ReapInterval, stoppingToken);
                await ReapStuckJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StuckJobReaper: error during reap cycle");
            }
        }

        _logger.LogInformation("StuckJobReaperService stopped");
    }

    private async Task ReapStuckJobsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var jobRepo = scope.ServiceProvider.GetRequiredService<IPeriodJobRepository>();
        var summaryRepo = scope.ServiceProvider.GetRequiredService<ISummaryRepository>();

        // Find jobs: status=running AND started_at < now() - 5 minutes
        var stuckJobs = await jobRepo.GetStuckJobsAsync(StuckTimeout, MaxJobsPerCycle, ct);

        if (stuckJobs.Count == 0)
            return;

        _logger.LogInformation("StuckJobReaper: found {Count} stuck jobs", stuckJobs.Count);

        foreach (var job in stuckJobs)
        {
            try
            {
                // Mark job as failed with timeout error
                // Setting lease_id = null provides zombie worker protection:
                // any subsequent updates from the original worker will fail
                // because they check lease_id match
                job.Status = "failed";
                job.FinishedAt = DateTime.UtcNow;
                job.LeaseId = null; // Zombie protection: subsequent worker updates fail
                job.Error = "timeout: exceeded 5 min";
                await jobRepo.UpdateAsync(job, ct);

                // Update related summary to status=failed (fenced by target_summary_version)
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
}
