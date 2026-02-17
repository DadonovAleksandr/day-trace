using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class SessionRepository : ISessionRepository
{
    private readonly DayTraceDbContext _context;

    public SessionRepository(DayTraceDbContext context)
    {
        _context = context;
    }

    public async Task<UserSession> CreateAsync(UserSession session, CancellationToken ct = default)
    {
        _context.UserSessions.Add(session);
        await _context.SaveChangesAsync(ct);
        return session;
    }

    public async Task<UserSession?> GetByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        return await _context.UserSessions
            .Include(s => s.User)
            .ThenInclude(u => u!.Settings)
            .FirstOrDefaultAsync(s => s.TokenHash == tokenHash && s.ExpiresAt > DateTime.UtcNow, ct);
    }

    public async Task UpdateAsync(UserSession session, CancellationToken ct = default)
    {
        _context.UserSessions.Update(session);
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteExpiredAsync(CancellationToken ct = default)
    {
        await _context.UserSessions
            .Where(s => s.ExpiresAt <= DateTime.UtcNow)
            .ExecuteDeleteAsync(ct);
    }
}
