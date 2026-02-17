using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class AuthReplayCacheRepository : IAuthReplayCacheRepository
{
    private readonly DayTraceDbContext _context;

    public AuthReplayCacheRepository(DayTraceDbContext context)
    {
        _context = context;
    }

    public async Task<AuthReplayCache?> GetByHashAsync(string dataHash, CancellationToken ct = default)
    {
        return await _context.AuthReplayCache
            .FirstOrDefaultAsync(e => e.DataHash == dataHash && e.ExpiresAt > DateTime.UtcNow, ct);
    }

    public async Task<(bool Inserted, AuthReplayCache Entry)> TryInsertAsync(
        AuthReplayCache entry, CancellationToken ct = default)
    {
        try
        {
            _context.AuthReplayCache.Add(entry);
            await _context.SaveChangesAsync(ct);
            return (true, entry);
        }
        catch (DbUpdateException)
        {
            // Concurrent insert — unique constraint violation. Reload winner's entry.
            _context.Entry(entry).State = EntityState.Detached;
            var winner = await _context.AuthReplayCache
                .FirstOrDefaultAsync(e => e.DataHash == entry.DataHash, ct);
            return (false, winner ?? entry);
        }
    }

    public async Task DeleteExpiredAsync(CancellationToken ct = default)
    {
        await _context.AuthReplayCache
            .Where(e => e.ExpiresAt <= DateTime.UtcNow)
            .ExecuteDeleteAsync(ct);
    }
}
