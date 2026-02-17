using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class UserSettingsRepository : IUserSettingsRepository
{
    private readonly DayTraceDbContext _context;

    public UserSettingsRepository(DayTraceDbContext context)
    {
        _context = context;
    }

    public async Task<UserSettings?> GetByUserIdAsync(long userId, CancellationToken ct = default)
    {
        return await _context.UsersSettings
            .FirstOrDefaultAsync(s => s.UserId == userId, ct);
    }

    public async Task<UserSettings> CreateAsync(UserSettings settings, CancellationToken ct = default)
    {
        _context.UsersSettings.Add(settings);
        await _context.SaveChangesAsync(ct);
        return settings;
    }

    public async Task UpdateAsync(UserSettings settings, CancellationToken ct = default)
    {
        _context.UsersSettings.Update(settings);
        await _context.SaveChangesAsync(ct);
    }
}
