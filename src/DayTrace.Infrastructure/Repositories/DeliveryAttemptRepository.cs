using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Domain.Models;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class DeliveryAttemptRepository : IDeliveryAttemptRepository
{
    private readonly DayTraceDbContext _context;

    public DeliveryAttemptRepository(DayTraceDbContext context)
    {
        _context = context;
    }

    public async Task<DeliveryAttempt> CreateAsync(DeliveryAttempt attempt, CancellationToken ct = default)
    {
        _context.DeliveryAttempts.Add(attempt);
        await _context.SaveChangesAsync(ct);
        return attempt;
    }

    public async Task CreateRangeAsync(IReadOnlyCollection<DeliveryAttempt> attempts, CancellationToken ct = default)
    {
        if (attempts.Count == 0)
            return;

        _context.DeliveryAttempts.AddRange(attempts);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(DeliveryAttempt attempt, CancellationToken ct = default)
    {
        _context.DeliveryAttempts.Update(attempt);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<bool> HasReminderForDateAsync(
        long userId, DateTime scheduledAtUtcStart, DateTime scheduledAtUtcEnd,
        CancellationToken ct = default)
    {
        return await _context.DeliveryAttempts
            .AnyAsync(d =>
                d.UserId == userId &&
                d.DeliveryType == "reminder" &&
                d.ScheduledAt >= scheduledAtUtcStart &&
                d.ScheduledAt < scheduledAtUtcEnd &&
                d.Status != "terminal_failed", ct);
    }

    public async Task<List<DeliveryAttempt>> GetRetryableAsync(
        int maxAttempts, int maxItems, CancellationToken ct = default)
    {
        return await _context.DeliveryAttempts
            .Where(d =>
                (d.Status == "failed" && d.AttemptNumber < maxAttempts) ||
                (d.DeliveryType == "admin_broadcast" && d.Status == "pending"))
            .OrderBy(d => d.Status == "failed" ? 0 : 1)
            .ThenBy(d => d.CreatedAt)
            .Take(maxItems)
            .ToListAsync(ct);
    }

    public async Task<List<DeliveryAttempt>> AdminListAsync(int limit, int offset, string? status = null, long? userId = null, string? deliveryType = null, CancellationToken ct = default)
    {
        var query = _context.DeliveryAttempts.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(d => d.Status == status);
        if (userId.HasValue)
            query = query.Where(d => d.UserId == userId.Value);
        if (!string.IsNullOrWhiteSpace(deliveryType))
            query = query.Where(d => d.DeliveryType == deliveryType);

        return await query
            .OrderByDescending(d => d.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<int> AdminCountAsync(string? status = null, long? userId = null, string? deliveryType = null, CancellationToken ct = default)
    {
        var query = _context.DeliveryAttempts.AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(d => d.Status == status);
        if (userId.HasValue)
            query = query.Where(d => d.UserId == userId.Value);
        if (!string.IsNullOrWhiteSpace(deliveryType))
            query = query.Where(d => d.DeliveryType == deliveryType);

        return await query.CountAsync(ct);
    }

    public async Task<BroadcastCampaignDeliveryStats> GetAdminBroadcastStatsAsync(long campaignId, CancellationToken ct = default)
    {
        var statsMap = await GetAdminBroadcastStatsByCampaignIdsAsync([campaignId], ct);
        return statsMap.TryGetValue(campaignId, out var stats)
            ? stats
            : new BroadcastCampaignDeliveryStats { CampaignId = campaignId };
    }

    public async Task<Dictionary<long, BroadcastCampaignDeliveryStats>> GetAdminBroadcastStatsByCampaignIdsAsync(
        IReadOnlyCollection<long> campaignIds,
        CancellationToken ct = default)
    {
        if (campaignIds.Count == 0)
            return [];

        var rows = await _context.DeliveryAttempts
            .AsNoTracking()
            .Where(d =>
                d.DeliveryType == "admin_broadcast" &&
                d.ReferenceId != null &&
                campaignIds.Contains(d.ReferenceId.Value))
            .GroupBy(d => d.ReferenceId!.Value)
            .Select(g => new BroadcastCampaignDeliveryStats
            {
                CampaignId = g.Key,
                Total = g.Count(),
                Pending = g.Count(d => d.Status == "pending"),
                Sent = g.Count(d => d.Status == "sent"),
                Failed = g.Count(d => d.Status == "failed"),
                TerminalFailed = g.Count(d => d.Status == "terminal_failed")
            })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.CampaignId);
    }
}
