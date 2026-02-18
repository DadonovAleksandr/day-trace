using DayTrace.Domain.Entities;

namespace DayTrace.Domain.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByTelegramUserIdAsync(long telegramUserId, CancellationToken ct = default);
    Task<User?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<User> CreateAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);

    /// <summary>
    /// Gets all active users with reminders enabled, including their settings.
    /// </summary>
    Task<List<User>> GetActiveUsersWithRemindersAsync(CancellationToken ct = default);

    /// <summary>Admin: get user by ID with settings.</summary>
    Task<User?> GetByIdWithSettingsAsync(long id, CancellationToken ct = default);

    /// <summary>Admin: list users with search/filter/pagination.</summary>
    Task<List<User>> GetAllAsync(int limit, int offset, string? search = null, string? status = null, CancellationToken ct = default);

    /// <summary>Admin: count users with search/filter.</summary>
    Task<int> CountAsync(string? search = null, string? status = null, CancellationToken ct = default);

    /// <summary>Soft-delete: sets status='deleted', deleted_at=now().</summary>
    Task SoftDeleteAsync(long userId, CancellationToken ct = default);

    /// <summary>Gets users eligible for hard-delete PII purge (status='deleted' AND deleted_at older than cutoff).</summary>
    Task<List<User>> GetPurgeableUsersAsync(DateTime cutoff, int maxBatch, CancellationToken ct = default);

    /// <summary>Hard-delete: removes user and all related data permanently.</summary>
    Task HardDeleteAsync(long userId, CancellationToken ct = default);
}
