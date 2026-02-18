using DayTrace.Domain.Interfaces;

namespace DayTrace.Api.BackgroundServices;

/// <summary>
/// Background job that hard-deletes PII for users soft-deleted > 30 days ago.
/// Runs daily. Per US-060 / NFR-7.
/// Idempotent: partial failure → safe retry on next run.
/// </summary>
public class UserPurgeService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<UserPurgeService> _logger;
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan PurgeAfter = TimeSpan.FromDays(30);
    private const int MaxBatchSize = 10;

    public UserPurgeService(IServiceProvider serviceProvider, ILogger<UserPurgeService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial delay to let the app start up
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeDeletedUsersAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user PII purge");
            }

            await Task.Delay(RunInterval, stoppingToken);
        }
    }

    private async Task PurgeDeletedUsersAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var userRepo = scope.ServiceProvider.GetRequiredService<IUserRepository>();

        var cutoff = DateTime.UtcNow - PurgeAfter;
        var users = await userRepo.GetPurgeableUsersAsync(cutoff, MaxBatchSize, ct);

        if (users.Count == 0)
        {
            _logger.LogDebug("No users eligible for PII purge");
            return;
        }

        _logger.LogInformation("Found {Count} users eligible for PII purge", users.Count);

        var purgedCount = 0;
        foreach (var user in users)
        {
            try
            {
                await userRepo.HardDeleteAsync(user.Id, ct);
                purgedCount++;
                _logger.LogInformation("Hard-deleted user {UserId} (soft-deleted at {DeletedAt})",
                    user.Id, user.DeletedAt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to hard-delete user {UserId}, will retry next run", user.Id);
                // Continue with next user — idempotent
            }
        }

        _logger.LogInformation("PII purge complete: {Purged}/{Total} users purged", purgedCount, users.Count);
    }
}
