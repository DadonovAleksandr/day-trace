using DayTrace.Domain.Interfaces;

namespace DayTrace.Api.BackgroundServices;

/// <summary>
/// Background job that deletes audit log entries older than 180 days.
/// Runs daily, batch deletion to avoid lock contention.
/// Per US-062 / NFR-4.
/// </summary>
public class AuditLogCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AuditLogCleanupService> _logger;
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(180);
    private const int BatchSize = 1000;

    public AuditLogCleanupService(IServiceProvider serviceProvider, ILogger<AuditLogCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay
        await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during audit log cleanup");
            }

            await Task.Delay(RunInterval, stoppingToken);
        }
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var auditLogRepo = scope.ServiceProvider.GetRequiredService<IAuditLogRepository>();

        var cutoff = DateTime.UtcNow - RetentionPeriod;
        var totalPurged = 0;
        int batchPurged;

        do
        {
            batchPurged = await auditLogRepo.DeleteOlderThanAsync(cutoff, BatchSize);
            totalPurged += batchPurged;

            if (batchPurged > 0)
                _logger.LogDebug("Audit log cleanup batch: {Count} entries deleted", batchPurged);

        } while (batchPurged == BatchSize && !ct.IsCancellationRequested);

        if (totalPurged > 0)
            _logger.LogInformation("Audit log cleanup complete: {Count} entries purged (older than 180 days)", totalPurged);
        else
            _logger.LogDebug("Audit log cleanup: no entries older than 180 days");
    }
}
