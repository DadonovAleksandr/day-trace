using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class WisdomRepository : IWisdomRepository
{
    private readonly DayTraceDbContext _context;

    public WisdomRepository(DayTraceDbContext context)
    {
        _context = context;
    }

    public async Task<Wisdom?> GetRandomAsync(CancellationToken ct = default)
    {
        return await _context.Wisdoms
            .OrderBy(_ => EF.Functions.Random())
            .FirstOrDefaultAsync(ct);
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        return await _context.Wisdoms.CountAsync(ct);
    }

    public async Task AddRangeAsync(IEnumerable<Wisdom> wisdoms, CancellationToken ct = default)
    {
        _context.Wisdoms.AddRange(wisdoms);
        await _context.SaveChangesAsync(ct);
    }
}
