using DayTrace.Domain.Entities;
using DayTrace.Domain.Models;

namespace DayTrace.Domain.Interfaces;

public interface IDeliveryAttemptRepository
{
    /// <summary>
    /// Creates a new delivery attempt.
    /// </summary>
    Task<DeliveryAttempt> CreateAsync(DeliveryAttempt attempt, CancellationToken ct = default);

    /// <summary>
    /// Creates multiple delivery attempts in one batch.
    /// </summary>
    Task CreateRangeAsync(IReadOnlyCollection<DeliveryAttempt> attempts, CancellationToken ct = default);

    /// <summary>
    /// Updates a delivery attempt (e.g., marking sent/failed).
    /// </summary>
    Task UpdateAsync(DeliveryAttempt attempt, CancellationToken ct = default);

    /// <summary>
    /// Checks if a reminder was already sent/pending for a user on a given scheduled date (UTC).
    /// </summary>
    Task<bool> HasReminderForDateAsync(long userId, DateTime scheduledAtUtcStart, DateTime scheduledAtUtcEnd, CancellationToken ct = default);

    /// <summary>
    /// Gets pending/failed delivery attempts eligible for retry (transient errors).
    /// </summary>
    Task<List<DeliveryAttempt>> GetRetryableAsync(int maxAttempts, int maxItems, CancellationToken ct = default);

    /// <summary>Admin: list delivery attempts with filtering and pagination.</summary>
    Task<List<DeliveryAttempt>> AdminListAsync(int limit, int offset, string? status = null, long? userId = null, string? deliveryType = null, CancellationToken ct = default);

    /// <summary>Admin: count delivery attempts with filtering.</summary>
    Task<int> AdminCountAsync(string? status = null, long? userId = null, string? deliveryType = null, CancellationToken ct = default);

    /// <summary>Admin broadcast: aggregated delivery counters for a campaign.</summary>
    Task<BroadcastCampaignDeliveryStats> GetAdminBroadcastStatsAsync(long campaignId, CancellationToken ct = default);

    /// <summary>Admin broadcast: aggregated delivery counters for multiple campaigns.</summary>
    Task<Dictionary<long, BroadcastCampaignDeliveryStats>> GetAdminBroadcastStatsByCampaignIdsAsync(
        IReadOnlyCollection<long> campaignIds,
        CancellationToken ct = default);
}
