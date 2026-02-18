namespace DayTrace.Domain.Interfaces;

using DayTrace.Domain.Entities;

public interface IAdminSessionRepository
{
    Task<AdminSession?> GetByTokenHashAsync(string tokenHash);
    Task<AdminSession> CreateAsync(AdminSession session);
    Task DeleteByTokenHashAsync(string tokenHash);
    Task DeleteExpiredAsync();
}
