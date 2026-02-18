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
}
