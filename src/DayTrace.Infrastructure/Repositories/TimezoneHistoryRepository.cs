using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class TimezoneHistoryRepository : ITimezoneHistoryRepository
{
    private readonly DayTraceDbContext _context;

    public TimezoneHistoryRepository(DayTraceDbContext context)
    {
        _context = context;
    }

    public async Task<TimezoneHistory> CreateAsync(TimezoneHistory record, CancellationToken ct = default)
    {
        _context.TimezoneHistory.Add(record);
        await _context.SaveChangesAsync(ct);
        return record;
    }

    public async Task<TimezoneHistory?> GetLatestAsync(long userId, CancellationToken ct = default)
    {
        return await _context.TimezoneHistory
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.EffectiveFrom)
            .FirstOrDefaultAsync(ct);
    }
}
