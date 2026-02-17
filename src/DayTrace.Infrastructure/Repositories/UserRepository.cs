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
}
