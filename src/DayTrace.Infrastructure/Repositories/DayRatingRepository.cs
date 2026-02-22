using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class DayRatingRepository : IDayRatingRepository
{
    private readonly DayTraceDbContext _context;

    public DayRatingRepository(DayTraceDbContext context)
    {
        _context = context;
    }

    public async Task<DayRating?> GetAsync(long userId, DateOnly localDate, CancellationToken ct = default)
    {
        return await _context.DayRatings
            .FirstOrDefaultAsync(r => r.UserId == userId && r.LocalDate == localDate, ct);
    }

    public async Task<DayRating> CreateAsync(DayRating rating, CancellationToken ct = default)
    {
        _context.DayRatings.Add(rating);
        await _context.SaveChangesAsync(ct);
        return rating;
    }

    public async Task UpdateAsync(DayRating rating, CancellationToken ct = default)
    {
        _context.DayRatings.Update(rating);
        await _context.SaveChangesAsync(ct);
    }
}
