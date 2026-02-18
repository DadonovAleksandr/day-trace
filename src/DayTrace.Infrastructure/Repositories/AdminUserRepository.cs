using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class AdminUserRepository : IAdminUserRepository
{
    private readonly DayTraceDbContext _db;

    public AdminUserRepository(DayTraceDbContext db)
    {
        _db = db;
    }

    public async Task<AdminUser?> GetByEmailAsync(string email)
    {
        return await _db.AdminUsers
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
    }

    public async Task<AdminUser?> GetByIdAsync(long id)
    {
        return await _db.AdminUsers.FindAsync(id);
    }

    public async Task<AdminUser> CreateAsync(AdminUser user)
    {
        _db.AdminUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    public async Task<List<AdminUser>> GetAllAsync(int limit, int offset, string? search = null)
    {
        var query = _db.AdminUsers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(u => u.Email.ToLower().Contains(s));
        }
        return await query
            .OrderBy(u => u.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<int> CountAsync(string? search = null)
    {
        var query = _db.AdminUsers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(u => u.Email.ToLower().Contains(s));
        }
        return await query.CountAsync();
    }
}
