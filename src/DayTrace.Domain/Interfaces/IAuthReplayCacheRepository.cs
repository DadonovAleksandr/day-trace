using DayTrace.Domain.Entities;

namespace DayTrace.Domain.Interfaces;

public interface IAuthReplayCacheRepository
{
    /// <summary>
    /// Gets a non-expired cache entry by data hash.
    /// </summary>
    Task<AuthReplayCache?> GetByHashAsync(string dataHash, CancellationToken ct = default);

    /// <summary>
    /// Atomically tries to insert a cache entry. Returns true if inserted (first request).
    /// On concurrent insert (unique constraint violation), returns false and the winning entry.
    /// </summary>
    Task<(bool Inserted, AuthReplayCache Entry)> TryInsertAsync(AuthReplayCache entry, CancellationToken ct = default);

    /// <summary>
    /// Deletes expired entries.
    /// </summary>
    Task DeleteExpiredAsync(CancellationToken ct = default);
}
