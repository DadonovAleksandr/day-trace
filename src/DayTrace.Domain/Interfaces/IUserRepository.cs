using DayTrace.Domain.Entities;

namespace DayTrace.Domain.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByTelegramUserIdAsync(long telegramUserId, CancellationToken ct = default);
    Task<User?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<User> CreateAsync(User user, CancellationToken ct = default);
    Task UpdateAsync(User user, CancellationToken ct = default);
}
