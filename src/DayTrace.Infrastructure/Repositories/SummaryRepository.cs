using System.Text;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class SummaryRepository : ISummaryRepository
{
    private readonly DayTraceDbContext _context;

    public SummaryRepository(DayTraceDbContext context)
    {
        _context = context;
    }

    public async Task<Summary?> GetAsync(
        long userId, string periodType, DateOnly periodStart, DateOnly periodEnd,
        CancellationToken ct = default)
    {
        return await _context.Summaries
            .FirstOrDefaultAsync(s =>
                s.UserId == userId &&
                s.PeriodType == periodType &&
                s.PeriodStart == periodStart &&
                s.PeriodEnd == periodEnd, ct);
    }

    public async Task<Summary> CreateAsync(Summary summary, CancellationToken ct = default)
    {
        _context.Summaries.Add(summary);
        await _context.SaveChangesAsync(ct);
        return summary;
    }

    public async Task UpdateAsync(Summary summary, CancellationToken ct = default)
    {
        _context.Summaries.Update(summary);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<int> FencedUpdateAsync(
        long summaryId, int targetVersion, Guid leaseId, string status,
        string? content, Guid[]? sourceEventIds, CancellationToken ct = default)
    {
        // Fenced update: only succeeds if version matches and summary is in 'generating' status
        // The lease_id check is done via a JOIN with period_jobs in the worker
        return await _context.Database.ExecuteSqlInterpolatedAsync(
            $@"UPDATE summaries 
               SET status = {status}, 
                   content = {content}::jsonb, 
                   source_event_ids = {sourceEventIds},
                   last_generated_at = NOW(),
                   version = version
               WHERE id = {summaryId} 
                 AND version = {targetVersion}
                 AND status = 'generating'", ct);
    }

    public async Task<(List<Summary> Items, string? NextCursor)> ListAsync(
        long userId, string periodType, DateOnly? from, DateOnly? to,
        int limit, string? cursor, CancellationToken ct = default)
    {
        var query = _context.Summaries
            .Where(s => s.UserId == userId && s.PeriodType == periodType);

        if (from.HasValue)
            query = query.Where(s => s.PeriodStart >= from.Value);
        if (to.HasValue)
            query = query.Where(s => s.PeriodEnd <= to.Value);

        // Decode cursor
        if (!string.IsNullOrEmpty(cursor))
        {
            var decoded = DecodeCursor(cursor);
            if (decoded != null)
            {
                var (cursorStart, cursorEnd) = decoded.Value;
                query = query.Where(s =>
                    s.PeriodStart < cursorStart ||
                    (s.PeriodStart == cursorStart && s.PeriodEnd < cursorEnd));
            }
        }

        var items = await query
            .OrderByDescending(s => s.PeriodStart)
            .ThenByDescending(s => s.PeriodEnd)
            .Take(limit + 1)
            .ToListAsync(ct);

        string? nextCursor = null;
        if (items.Count > limit)
        {
            items = items.Take(limit).ToList();
            var last = items[^1];
            nextCursor = EncodeCursor(last.PeriodStart, last.PeriodEnd);
        }

        return (items, nextCursor);
    }

    public async Task<int> CountByUserAsync(long userId, CancellationToken ct = default)
    {
        return await _context.Summaries
            .CountAsync(s => s.UserId == userId, ct);
    }

    public async Task<List<Summary>> AdminListAsync(int limit, int offset, long? userId = null, string? periodType = null, DateOnly? from = null, DateOnly? to = null, string? status = null, CancellationToken ct = default)
    {
        var query = _context.Summaries.AsQueryable();
        if (userId.HasValue) query = query.Where(s => s.UserId == userId.Value);
        if (!string.IsNullOrWhiteSpace(periodType)) query = query.Where(s => s.PeriodType == periodType);
        if (from.HasValue) query = query.Where(s => s.PeriodStart >= from.Value);
        if (to.HasValue) query = query.Where(s => s.PeriodEnd <= to.Value);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(s => s.Status == status);

        return await query
            .OrderByDescending(s => s.PeriodStart)
            .ThenByDescending(s => s.PeriodEnd)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<int> AdminCountAsync(long? userId = null, string? periodType = null, DateOnly? from = null, DateOnly? to = null, string? status = null, CancellationToken ct = default)
    {
        var query = _context.Summaries.AsQueryable();
        if (userId.HasValue) query = query.Where(s => s.UserId == userId.Value);
        if (!string.IsNullOrWhiteSpace(periodType)) query = query.Where(s => s.PeriodType == periodType);
        if (from.HasValue) query = query.Where(s => s.PeriodStart >= from.Value);
        if (to.HasValue) query = query.Where(s => s.PeriodEnd <= to.Value);
        if (!string.IsNullOrWhiteSpace(status)) query = query.Where(s => s.Status == status);

        return await query.CountAsync(ct);
    }

    private static string EncodeCursor(DateOnly periodStart, DateOnly periodEnd)
    {
        var raw = $"{periodStart:yyyy-MM-dd}|{periodEnd:yyyy-MM-dd}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    private static (DateOnly PeriodStart, DateOnly PeriodEnd)? DecodeCursor(string cursor)
    {
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|', 2);
            if (parts.Length != 2) return null;
            return (
                DateOnly.ParseExact(parts[0], "yyyy-MM-dd"),
                DateOnly.ParseExact(parts[1], "yyyy-MM-dd")
            );
        }
        catch
        {
            return null;
        }
    }
}
