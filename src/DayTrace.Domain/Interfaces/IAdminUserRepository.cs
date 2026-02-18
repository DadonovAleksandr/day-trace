namespace DayTrace.Domain.Interfaces;

using DayTrace.Domain.Entities;

public interface IAdminUserRepository
{
    Task<AdminUser?> GetByEmailAsync(string email);
    Task<AdminUser?> GetByIdAsync(long id);
    Task<AdminUser> CreateAsync(AdminUser user);
    Task<List<AdminUser>> GetAllAsync(int limit, int offset, string? search = null);
    Task<int> CountAsync(string? search = null);
}
