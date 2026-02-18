using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class OperationIdCacheRepository : IOperationIdCacheRepository
{
    private readonly DayTraceDbContext _context;

    public OperationIdCacheRepository(DayTraceDbContext context)
    {
        _context = context;
    }

    public async Task<(bool Inserted, OperationIdCache Entry)> TryInsertAsync(
        OperationIdCache entry, CancellationToken ct = default)
    {
        // Check for existing first
        var existing = await GetAsync(entry.UserId, entry.Method, entry.Route, entry.ClientOperationId, ct);
        if (existing != null)
            return (false, existing);

        try
        {
            _context.OperationIdCache.Add(entry);
            await _context.SaveChangesAsync(ct);
            return (true, entry);
        }
        catch (DbUpdateException)
        {
            // Concurrent insert — unique constraint violation
            _context.Entry(entry).State = EntityState.Detached;
            var winner = await GetAsync(entry.UserId, entry.Method, entry.Route, entry.ClientOperationId, ct);
            return (false, winner ?? entry);
        }
    }

    public async Task<OperationIdCache?> GetAsync(
        long userId, string method, string route, string clientOperationId,
        CancellationToken ct = default)
    {
        return await _context.OperationIdCache
            .FirstOrDefaultAsync(e =>
                e.UserId == userId &&
                e.Method == method &&
                e.Route == route &&
                e.ClientOperationId == clientOperationId, ct);
    }

    public async Task UpdateResponseAsync(
        long userId, string method, string route, string clientOperationId,
        string responseHash, CancellationToken ct = default)
    {
        await _context.OperationIdCache
            .Where(e => e.UserId == userId && e.Method == method
                && e.Route == route && e.ClientOperationId == clientOperationId)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.ResponseHash, responseHash), ct);
    }

    public async Task<int> DeleteExpiredAsync(TimeSpan ttl, int batchLimit = 1000, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - ttl;
        return await _context.OperationIdCache
            .Where(e => e.CreatedAt < cutoff)
            .Take(batchLimit)
            .ExecuteDeleteAsync(ct);
    }
}
