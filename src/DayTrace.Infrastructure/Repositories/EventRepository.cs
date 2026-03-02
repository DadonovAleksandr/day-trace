using System.Text;
using System.Text.Json;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class EventRepository : IEventRepository
{
    private readonly DayTraceDbContext _context;

    public EventRepository(DayTraceDbContext context)
    {
        _context = context;
    }

    public async Task<Event> CreateAsync(Event evt, CancellationToken ct = default)
    {
        _context.Events.Add(evt);
        await _context.SaveChangesAsync(ct);
        return evt;
    }

    public async Task<Event?> GetByIdAsync(Guid id, long userId, CancellationToken ct = default)
    {
        return await _context.Events
            .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId && e.DeletedAt == null, ct);
    }

    public async Task<Event?> GetByUserAndDateAsync(long userId, DateOnly localDate, CancellationToken ct = default)
    {
        return await _context.Events
            .FirstOrDefaultAsync(e => e.UserId == userId && e.LocalDate == localDate && e.DeletedAt == null, ct);
    }

    public async Task UpdateAsync(Event evt, CancellationToken ct = default)
    {
        _context.Events.Update(evt);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<(List<Event> Items, string? NextCursor)> ListAsync(
        long userId, DateOnly? from, DateOnly? to,
        int limit, string? cursor, CancellationToken ct = default)
    {
        var query = _context.Events
            .Where(e => e.UserId == userId && e.DeletedAt == null);

        if (from.HasValue)
            query = query.Where(e => e.LocalDate >= from.Value);
        if (to.HasValue)
            query = query.Where(e => e.LocalDate <= to.Value);

        // Decode cursor: base64("{localDate}|{createdAt}|{id}")
        if (!string.IsNullOrEmpty(cursor))
        {
            var decoded = DecodeCursor(cursor);
            if (decoded != null)
            {
                var (cursorDate, cursorCreatedAt, cursorId) = decoded.Value;
                query = query.Where(e =>
                    e.LocalDate < cursorDate ||
                    (e.LocalDate == cursorDate && e.CreatedAt < cursorCreatedAt) ||
                    (e.LocalDate == cursorDate && e.CreatedAt == cursorCreatedAt && e.Id.CompareTo(cursorId) < 0));
            }
        }

        // Sort: local_date DESC, created_at DESC, id DESC
        var items = await query
            .OrderByDescending(e => e.LocalDate)
            .ThenByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Take(limit + 1) // fetch one extra to determine if there's more
            .ToListAsync(ct);

        string? nextCursor = null;
        if (items.Count > limit)
        {
            items = items.Take(limit).ToList();
            var last = items[^1];
            nextCursor = EncodeCursor(last.LocalDate, last.CreatedAt, last.Id);
        }

        return (items, nextCursor);
    }

    public async Task<List<Event>> GetByPeriodAsync(
        long userId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default)
    {
        return await _context.Events
            .Where(e => e.UserId == userId
                && e.DeletedAt == null
                && e.LocalDate >= periodStart
                && e.LocalDate <= periodEnd)
            .ToListAsync(ct);
    }

    public async Task<int> CountByPeriodAsync(
        long userId, DateOnly periodStart, DateOnly periodEnd, CancellationToken ct = default)
    {
        return await _context.Events
            .CountAsync(e => e.UserId == userId
                && e.DeletedAt == null
                && e.LocalDate >= periodStart
                && e.LocalDate <= periodEnd, ct);
    }

    public async Task<int> CountByUserAsync(long userId, CancellationToken ct = default)
    {
        return await _context.Events
            .CountAsync(e => e.UserId == userId && e.DeletedAt == null, ct);
    }

    public async Task<List<Event>> AdminListAsync(int limit, int offset, long? userId = null, DateOnly? from = null, DateOnly? to = null, int? importance = null, CancellationToken ct = default)
    {
        var query = _context.Events.Where(e => e.DeletedAt == null).AsQueryable();
        if (userId.HasValue) query = query.Where(e => e.UserId == userId.Value);
        if (from.HasValue) query = query.Where(e => e.LocalDate >= from.Value);
        if (to.HasValue) query = query.Where(e => e.LocalDate <= to.Value);
        if (importance.HasValue) query = query.Where(e => e.Importance == importance.Value);

        return await query
            .OrderByDescending(e => e.LocalDate)
            .ThenByDescending(e => e.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<int> AdminCountAsync(long? userId = null, DateOnly? from = null, DateOnly? to = null, int? importance = null, CancellationToken ct = default)
    {
        var query = _context.Events.Where(e => e.DeletedAt == null).AsQueryable();
        if (userId.HasValue) query = query.Where(e => e.UserId == userId.Value);
        if (from.HasValue) query = query.Where(e => e.LocalDate >= from.Value);
        if (to.HasValue) query = query.Where(e => e.LocalDate <= to.Value);
        if (importance.HasValue) query = query.Where(e => e.Importance == importance.Value);

        return await query.CountAsync(ct);
    }

    public async Task<DateOnly?> GetFirstEventDateAsync(long userId)
    {
        var firstEvent = await _context.Events
            .Where(e => e.UserId == userId && e.DeletedAt == null)
            .OrderBy(e => e.LocalDate)
            .FirstOrDefaultAsync();
        return firstEvent?.LocalDate;
    }

    private static string EncodeCursor(DateOnly localDate, DateTime createdAt, Guid id)
    {
        var raw = $"{localDate:yyyy-MM-dd}|{createdAt:O}|{id}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
    }

    private static (DateOnly LocalDate, DateTime CreatedAt, Guid Id)? DecodeCursor(string cursor)
    {
        try
        {
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = raw.Split('|', 3);
            if (parts.Length != 3) return null;
            return (
                DateOnly.ParseExact(parts[0], "yyyy-MM-dd"),
                DateTime.Parse(parts[1], null, System.Globalization.DateTimeStyles.RoundtripKind),
                Guid.Parse(parts[2])
            );
        }
        catch
        {
            return null;
        }
    }
}
