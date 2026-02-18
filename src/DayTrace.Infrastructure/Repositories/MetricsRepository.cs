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

        var sentReminders = await _db.DeliveryAttempts
            .Where(d => d.DeliveryType == "reminder" && d.Status == "sent" && d.SentAt >= windowStart && d.SentAt <= asOf)
            .ToListAsync();

        var total = sentReminders.Count;
        if (total == 0) return (0, 0);

        // For each reminder, check if user created an event within 24h
        var converted = 0;
        foreach (var reminder in sentReminders)
        {
            var eventExists = await _db.Events
                .AnyAsync(e => e.UserId == reminder.UserId
                    && e.CreatedAt >= reminder.SentAt
                    && e.CreatedAt <= reminder.SentAt!.Value.AddHours(24)
                    && e.DeletedAt == null);
            if (eventExists) converted++;
        }

        return (converted, total);
    }

    public async Task<(int converted, int total)> GetPromptConversionAsync(DateTime asOf)
    {
        // Prompts sent in the last 48h window
        var windowStart = asOf.AddHours(-48);

        var prompts = await _db.PromptDeliveries
            .Where(p => p.SentAt >= windowStart && p.SentAt <= asOf)
            .ToListAsync();

        var total = prompts.Count;
        if (total == 0) return (0, 0);

        // For each prompt, check if summary was generated within 48h
        var converted = 0;
        foreach (var prompt in prompts)
        {
            var summaryExists = await _db.Summaries
                .AnyAsync(s => s.UserId == prompt.UserId
                    && s.PeriodType == prompt.PeriodType
                    && s.PeriodStart == prompt.PeriodStart
                    && s.PeriodEnd == prompt.PeriodEnd
                    && s.Status == "generated"
                    && s.LastGeneratedAt >= prompt.SentAt
                    && s.LastGeneratedAt <= prompt.SentAt.AddHours(48));
            if (summaryExists) converted++;
        }

        return (converted, total);
    }
}
