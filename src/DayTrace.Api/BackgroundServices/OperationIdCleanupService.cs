using DayTrace.Domain.Interfaces;

namespace DayTrace.Api.BackgroundServices;

/// <summary>
/// Background service that periodically cleans up expired operation ID cache entries.
/// Runs every minute, batch deletes with LIMIT to avoid long locks.
/// </summary>
public class OperationIdCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OperationIdCleanupService> _logger;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    public OperationIdCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<OperationIdCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OperationIdCleanupService started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CleanupInterval, stoppingToken);

                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IOperationIdCacheRepository>();
                var deleted = await repo.DeleteExpiredAsync(Ttl, 1000, stoppingToken);

                if (deleted > 0)
                {
                    _logger.LogInformation("OperationIdCleanup: deleted {Count} expired entries", deleted);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OperationIdCleanup: error during cleanup");
            }
        }

        _logger.LogInformation("OperationIdCleanupService stopped");
    }
}
