using DayTrace.Domain.Entities;

namespace DayTrace.Domain.Interfaces;

public interface ISummaryRepository
{
    /// <summary>
    /// Gets a summary by user/period unique key.
    /// </summary>
    Task<Summary?> GetAsync(long userId, string periodType, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default);

    /// <summary>
    /// Creates a new summary.
    /// </summary>
    Task<Summary> CreateAsync(Summary summary, CancellationToken ct = default);

    /// <summary>
    /// Updates a summary (used for version bump, status change, content update).
    /// </summary>
    Task UpdateAsync(Summary summary, CancellationToken ct = default);

    /// <summary>
    /// Fenced update: only updates if version and lease match. Returns rows affected.
    /// Used by worker finalization (TX2).
    /// </summary>
    Task<int> FencedUpdateAsync(long summaryId, int targetVersion, Guid leaseId, string status, string? content, Guid[]? sourceEventIds, CancellationToken ct = default);

    /// <summary>
    /// Lists summaries for a period type with optional date filtering and cursor pagination.
    /// </summary>
    Task<(List<Summary> Items, string? NextCursor)> ListAsync(long userId, string periodType, DateOnly? from, DateOnly? to, int limit, string? cursor, CancellationToken ct = default);
}
