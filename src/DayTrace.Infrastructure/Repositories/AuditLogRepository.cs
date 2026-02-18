using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class AuditLogRepository : IAuditLogRepository
{
    private readonly DayTraceDbContext _db;

    public AuditLogRepository(DayTraceDbContext db)
    {
        _db = db;
    }

    public async Task<AuditLog> CreateAsync(AuditLog log)
    {
        _db.AuditLogs.Add(log);
        await _db.SaveChangesAsync();
        return log;
    }

    public async Task<List<AuditLog>> GetAllAsync(int limit, int offset, string? actorType = null, string? action = null, DateTime? from = null, DateTime? to = null)
    {
        var query = BuildFilterQuery(actorType, action, from, to);
        return await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }

    public async Task<int> CountAsync(string? actorType = null, string? action = null, DateTime? from = null, DateTime? to = null)
    {
        return await BuildFilterQuery(actorType, action, from, to).CountAsync();
    }

    public async Task<int> DeleteOlderThanAsync(DateTime cutoff, int batchSize = 1000)
    {
        // Batch delete to avoid long locks
        var toDelete = await _db.AuditLogs
            .Where(l => l.CreatedAt < cutoff)
            .OrderBy(l => l.Id)
            .Take(batchSize)
            .ToListAsync();

        if (toDelete.Count == 0) return 0;

        _db.AuditLogs.RemoveRange(toDelete);
        await _db.SaveChangesAsync();
        return toDelete.Count;
    }

    private IQueryable<AuditLog> BuildFilterQuery(string? actorType, string? action, DateTime? from, DateTime? to)
    {
        var query = _db.AuditLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(actorType))
            query = query.Where(l => l.ActorType == actorType);
        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(l => l.Action == action);
        if (from.HasValue)
            query = query.Where(l => l.CreatedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(l => l.CreatedAt <= to.Value);
        return query;
    }
}
