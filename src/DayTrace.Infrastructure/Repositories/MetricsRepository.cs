using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class MetricsRepository : IMetricsRepository
{
    private readonly DayTraceDbContext _db;

    public MetricsRepository(DayTraceDbContext db)
    {
        _db = db;
    }

    public async Task<int> GetDailyActiveUsersAsync(DateTime targetDay)
    {
        var start = targetDay.Date;
        var end = start.AddDays(1);

        return await _db.Events
            .Where(e => e.CreatedAt >= start && e.CreatedAt < end && e.DeletedAt == null)
            .Select(e => e.UserId)
            .Distinct()
            .CountAsync();
    }

    public async Task<int> GetWeeklyActiveUsersAsync(DateTime asOf)
    {
        var start = asOf.Date.AddDays(-7);

        return await _db.Events
            .Where(e => e.CreatedAt >= start && e.CreatedAt <= asOf && e.DeletedAt == null)
            .Select(e => e.UserId)
            .Distinct()
            .CountAsync();
    }

    public async Task<int> GetMonthlyActiveUsersAsync(DateTime asOf)
    {
        var start = asOf.Date.AddDays(-30);

        return await _db.Events
            .Where(e => e.CreatedAt >= start && e.CreatedAt <= asOf && e.DeletedAt == null)
            .Select(e => e.UserId)
            .Distinct()
            .CountAsync();
    }

    public async Task<(int converted, int total)> GetReminderConversionAsync(DateTime asOf)
    {
        // Reminders sent in the last 24h window
        var windowStart = asOf.AddHours(-24);

        var total = await _db.DeliveryAttempts
            .CountAsync(d => d.DeliveryType == "reminder" && d.Status == "sent"
                && d.SentAt >= windowStart && d.SentAt <= asOf);

        if (total == 0) return (0, 0);

        // Single query: count reminders that have a matching event within 24h (JOIN approach)
        var converted = await _db.DeliveryAttempts
            .Where(d => d.DeliveryType == "reminder" && d.Status == "sent"
                && d.SentAt >= windowStart && d.SentAt <= asOf)
            .Where(d => _db.Events.Any(e =>
                e.UserId == d.UserId
                && e.CreatedAt >= d.SentAt
                && e.CreatedAt <= d.SentAt!.Value.AddHours(24)
                && e.DeletedAt == null))
            .CountAsync();

        return (converted, total);
    }

    public async Task<(int converted, int total)> GetPromptConversionAsync(DateTime asOf)
    {
        // Prompts sent in the last 48h window
        var windowStart = asOf.AddHours(-48);

        var total = await _db.PromptDeliveries
            .CountAsync(p => p.SentAt >= windowStart && p.SentAt <= asOf);

        if (total == 0) return (0, 0);

        // Single query: count prompts that have a matching generated summary within 48h
        var converted = await _db.PromptDeliveries
            .Where(p => p.SentAt >= windowStart && p.SentAt <= asOf)
            .Where(p => _db.Summaries.Any(s =>
                s.UserId == p.UserId
                && s.PeriodType == p.PeriodType
                && s.PeriodStart == p.PeriodStart
                && s.PeriodEnd == p.PeriodEnd
                && s.Status == "generated"
                && s.LastGeneratedAt >= p.SentAt
                && s.LastGeneratedAt <= p.SentAt.AddHours(48)))
            .CountAsync();

        return (converted, total);
    }
}
