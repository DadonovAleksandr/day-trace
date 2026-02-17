using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class WeekScheduleHistoryRepository : IWeekScheduleHistoryRepository
{
    private readonly DayTraceDbContext _context;

    public WeekScheduleHistoryRepository(DayTraceDbContext context)
    {
        _context = context;
    }

    public async Task<List<WeekScheduleHistory>> GetByUserIdAsync(long userId, CancellationToken ct = default)
    {
        return await _context.WeekScheduleHistory
            .Where(w => w.UserId == userId)
            .OrderBy(w => w.EffectiveFromLocalDate)
            .ToListAsync(ct);
    }

    public async Task<WeekScheduleHistory?> GetEffectiveForDateAsync(long userId, DateOnly localDate, CancellationToken ct = default)
    {
        return await _context.WeekScheduleHistory
            .Where(w => w.UserId == userId && w.EffectiveFromLocalDate <= localDate)
            .OrderByDescending(w => w.EffectiveFromLocalDate)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<WeekScheduleHistory?> GetEarliestAsync(long userId, CancellationToken ct = default)
    {
        return await _context.WeekScheduleHistory
            .Where(w => w.UserId == userId)
            .OrderBy(w => w.EffectiveFromLocalDate)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<WeekScheduleHistory> CreateAsync(WeekScheduleHistory record, CancellationToken ct = default)
    {
        _context.WeekScheduleHistory.Add(record);
        await _context.SaveChangesAsync(ct);
        return record;
    }
}
