using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class AdminBroadcastCampaignRepository : IAdminBroadcastCampaignRepository
{
    private readonly DayTraceDbContext _context;

    public AdminBroadcastCampaignRepository(DayTraceDbContext context)
    {
        _context = context;
    }

    public async Task<AdminBroadcastCampaign> CreateAsync(AdminBroadcastCampaign campaign, CancellationToken ct = default)
    {
        _context.AdminBroadcastCampaigns.Add(campaign);
        await _context.SaveChangesAsync(ct);
        return campaign;
    }

    public async Task UpdateAsync(AdminBroadcastCampaign campaign, CancellationToken ct = default)
    {
        _context.AdminBroadcastCampaigns.Update(campaign);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<AdminBroadcastCampaign?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        return await _context.AdminBroadcastCampaigns
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<List<AdminBroadcastCampaign>> AdminListAsync(int limit, int offset, string? audience = null, CancellationToken ct = default)
    {
        var query = _context.AdminBroadcastCampaigns.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(audience))
            query = query.Where(c => c.Audience == audience);

        return await query
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<int> AdminCountAsync(string? audience = null, CancellationToken ct = default)
    {
        var query = _context.AdminBroadcastCampaigns.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(audience))
            query = query.Where(c => c.Audience == audience);

        return await query.CountAsync(ct);
    }
}
