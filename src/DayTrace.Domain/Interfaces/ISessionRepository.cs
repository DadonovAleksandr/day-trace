using DayTrace.Domain.Entities;

namespace DayTrace.Domain.Interfaces;

public interface ISessionRepository
{
    Task<UserSession> CreateAsync(UserSession session, CancellationToken ct = default);
    Task<UserSession?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default);
    Task UpdateAsync(UserSession session, CancellationToken ct = default);
    Task DeleteExpiredAsync(CancellationToken ct = default);
}
