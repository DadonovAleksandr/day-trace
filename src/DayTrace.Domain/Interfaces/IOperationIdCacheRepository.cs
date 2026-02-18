using DayTrace.Domain.Entities;

namespace DayTrace.Domain.Interfaces;

public interface IOperationIdCacheRepository
{
    /// <summary>
    /// Atomically tries to insert a cache entry (ON CONFLICT DO NOTHING).
    /// Returns (true, entry) if inserted, (false, existing) if duplicate.
    /// </summary>
    Task<(bool Inserted, OperationIdCache Entry)> TryInsertAsync(OperationIdCache entry, CancellationToken ct = default);

    /// <summary>
    /// Gets existing cache entry by unique key.
    /// </summary>
    Task<OperationIdCache?> GetAsync(long userId, string method, string route, string clientOperationId, CancellationToken ct = default);

    /// <summary>
    /// Updates the cached response body for an already-claimed operation.
    /// </summary>
    Task UpdateResponseAsync(long userId, string method, string route, string clientOperationId, string responseHash, CancellationToken ct = default);

    /// <summary>
    /// Deletes a specific cache entry (e.g., pending claim on non-2xx response).
    /// </summary>
    Task DeleteAsync(long userId, string method, string route, string clientOperationId, CancellationToken ct = default);

    /// <summary>
    /// Deletes entries older than the given TTL. Returns count deleted.
    /// </summary>
    Task<int> DeleteExpiredAsync(TimeSpan ttl, int batchLimit = 1000, CancellationToken ct = default);
}
