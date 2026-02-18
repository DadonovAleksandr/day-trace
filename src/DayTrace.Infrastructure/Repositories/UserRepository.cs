using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly DayTraceDbContext _context;

    public UserRepository(DayTraceDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByTelegramUserIdAsync(long telegramUserId, CancellationToken ct = default)
    {
        return await _context.Users
            .Include(u => u.Settings)
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, ct);
    }

    public async Task<User?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        return await _context.Users
            .Include(u => u.Settings)
            .FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<User> CreateAsync(User user, CancellationToken ct = default)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync(ct);
        return user;
    }

    public async Task UpdateAsync(User user, CancellationToken ct = default)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<List<User>> GetActiveUsersWithRemindersAsync(CancellationToken ct = default)
    {
        return await _context.Users
            .Include(u => u.Settings)
            .Where(u => u.Status == "active" && u.Settings != null && u.Settings.ReminderEnabled)
            .ToListAsync(ct);
    }

    public async Task<User?> GetByIdWithSettingsAsync(long id, CancellationToken ct = default)
    {
        return await _context.Users
            .Include(u => u.Settings)
            .FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<List<User>> GetAllAsync(int limit, int offset, string? search = null, string? status = null, CancellationToken ct = default)
    {
        var query = _context.Users.Include(u => u.Settings).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            if (long.TryParse(search, out var tgId))
                query = query.Where(u => u.TelegramUserId == tgId);
        }

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(u => u.Status == status);

        return await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<int> CountAsync(string? search = null, string? status = null, CancellationToken ct = default)
    {
        var query = _context.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            if (long.TryParse(search, out var tgId))
                query = query.Where(u => u.TelegramUserId == tgId);
        }

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(u => u.Status == status);

        return await query.CountAsync(ct);
    }
}
