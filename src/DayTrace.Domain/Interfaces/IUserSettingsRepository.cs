using DayTrace.Domain.Entities;

namespace DayTrace.Domain.Interfaces;

public interface IUserSettingsRepository
{
    Task<UserSettings?> GetByUserIdAsync(long userId, CancellationToken ct = default);
    Task<UserSettings> CreateAsync(UserSettings settings, CancellationToken ct = default);
    Task UpdateAsync(UserSettings settings, CancellationToken ct = default);
}
