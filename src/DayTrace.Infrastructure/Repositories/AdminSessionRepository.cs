using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class AdminSessionRepository : IAdminSessionRepository
{
    private readonly DayTraceDbContext _db;

    public AdminSessionRepository(DayTraceDbContext db)
    {
        _db = db;
    }

    public async Task<AdminSession?> GetByTokenHashAsync(string tokenHash)
    {
        return await _db.AdminSessions
            .Include(s => s.AdminUser)
            .FirstOrDefaultAsync(s => s.TokenHash == tokenHash && s.ExpiresAt > DateTime.UtcNow);
    }

    public async Task<AdminSession> CreateAsync(AdminSession session)
    {
        _db.AdminSessions.Add(session);
        await _db.SaveChangesAsync();
        return session;
    }

    public async Task DeleteByTokenHashAsync(string tokenHash)
    {
        var session = await _db.AdminSessions.FirstOrDefaultAsync(s => s.TokenHash == tokenHash);
        if (session != null)
        {
            _db.AdminSessions.Remove(session);
            await _db.SaveChangesAsync();
        }
    }

    public async Task DeleteExpiredAsync()
    {
        var expired = await _db.AdminSessions
            .Where(s => s.ExpiresAt <= DateTime.UtcNow)
            .ToListAsync();
        _db.AdminSessions.RemoveRange(expired);
        await _db.SaveChangesAsync();
    }
}
