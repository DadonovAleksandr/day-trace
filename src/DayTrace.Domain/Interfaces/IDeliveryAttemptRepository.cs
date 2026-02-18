using DayTrace.Domain.Entities;

namespace DayTrace.Domain.Interfaces;

public interface IDeliveryAttemptRepository
{
    /// <summary>
    /// Creates a new delivery attempt.
    /// </summary>
    Task<DeliveryAttempt> CreateAsync(DeliveryAttempt attempt, CancellationToken ct = default);

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
}
