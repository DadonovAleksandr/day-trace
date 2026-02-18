using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly DayTraceDbContext _context;

    public UserRepository(DayTraceDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByTelegramUserIdAsync(long telegramUserId, CancellationToken ct = default)
    {
        return await _context.Users
            .Include(u => u.Settings)
            .FirstOrDefaultAsync(u => u.TelegramUserId == telegramUserId, ct);
    }

    public async Task<User?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        return await _context.Users
            .Include(u => u.Settings)
            .FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<User> CreateAsync(User user, CancellationToken ct = default)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync(ct);
        return user;
    }

    public async Task UpdateAsync(User user, CancellationToken ct = default)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<List<User>> GetActiveUsersWithRemindersAsync(CancellationToken ct = default)
    {
        return await _context.Users
            .Include(u => u.Settings)
            .Where(u => u.Status == "active" && u.Settings != null && u.Settings.ReminderEnabled)
            .ToListAsync(ct);
    }

    public async Task<User?> GetByIdWithSettingsAsync(long id, CancellationToken ct = default)
    {
        return await _context.Users
            .Include(u => u.Settings)
            .FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task<List<User>> GetAllAsync(int limit, int offset, string? search = null, string? status = null, CancellationToken ct = default)
    {
        var query = _context.Users.Include(u => u.Settings).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            if (long.TryParse(search, out var tgId))
                query = query.Where(u => u.TelegramUserId == tgId);
        }

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(u => u.Status == status);

        return await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<int> CountAsync(string? search = null, string? status = null, CancellationToken ct = default)
    {
        var query = _context.Users.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            if (long.TryParse(search, out var tgId))
                query = query.Where(u => u.TelegramUserId == tgId);
        }

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(u => u.Status == status);

        return await query.CountAsync(ct);
    }

    public async Task SoftDeleteAsync(long userId, CancellationToken ct = default)
    {
        var user = await _context.Users.FindAsync(new object[] { userId }, ct);
        if (user != null)
        {
            user.Status = "deleted";
            user.DeletedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task<List<User>> GetPurgeableUsersAsync(DateTime cutoff, int maxBatch, CancellationToken ct = default)
    {
        return await _context.Users
            .Where(u => u.Status == "deleted" && u.DeletedAt != null && u.DeletedAt < cutoff)
            .OrderBy(u => u.DeletedAt)
            .Take(maxBatch)
            .ToListAsync(ct);
    }

    public async Task HardDeleteAsync(long userId, CancellationToken ct = default)
    {
        // Purge order per NFR-7:
        // period_jobs → summaries → events → prompt_deliveries (anonymize) →
        // audit_logs (anonymize) → delivery_attempts → FK-deps → users_settings → users

        // 1. Delete period_jobs
        var periodJobs = await _context.PeriodJobs.Where(p => p.UserId == userId).ToListAsync(ct);
        _context.PeriodJobs.RemoveRange(periodJobs);

        // 2. Delete summaries
        var summaries = await _context.Summaries.Where(s => s.UserId == userId).ToListAsync(ct);
        _context.Summaries.RemoveRange(summaries);

        // 3. Delete events
        var events = await _context.Events.Where(e => e.UserId == userId).ToListAsync(ct);
        _context.Events.RemoveRange(events);

        // 4. Anonymize prompt_deliveries (set user_id = NULL not possible with FK, so delete)
        var promptDeliveries = await _context.PromptDeliveries.Where(p => p.UserId == userId).ToListAsync(ct);
        _context.PromptDeliveries.RemoveRange(promptDeliveries);

        // 5. Anonymize audit_logs where actor_id = userId
        var auditLogs = await _context.AuditLogs
            .Where(a => a.ActorId == userId.ToString())
            .ToListAsync(ct);
        foreach (var log in auditLogs)
        {
            log.ActorId = null;
            log.Payload = "{}";
        }

        // 6. Delete delivery_attempts
        var deliveryAttempts = await _context.DeliveryAttempts.Where(d => d.UserId == userId).ToListAsync(ct);
        _context.DeliveryAttempts.RemoveRange(deliveryAttempts);

        // 7. Delete FK-dependent records
        var weekScheduleHistory = await _context.WeekScheduleHistory.Where(w => w.UserId == userId).ToListAsync(ct);
        _context.WeekScheduleHistory.RemoveRange(weekScheduleHistory);

        var timezoneHistory = await _context.TimezoneHistory.Where(t => t.UserId == userId).ToListAsync(ct);
        _context.TimezoneHistory.RemoveRange(timezoneHistory);

        var periodRunCounters = await _context.PeriodRunCounters.Where(p => p.UserId == userId).ToListAsync(ct);
        _context.PeriodRunCounters.RemoveRange(periodRunCounters);

        var userSessions = await _context.UserSessions.Where(s => s.UserId == userId).ToListAsync(ct);
        _context.UserSessions.RemoveRange(userSessions);

        var operationIdCache = await _context.OperationIdCache.Where(o => o.UserId == userId).ToListAsync(ct);
        _context.OperationIdCache.RemoveRange(operationIdCache);

        // 8. Delete users_settings (cascade should handle, but be explicit)
        var settings = await _context.UsersSettings.FindAsync(new object[] { userId }, ct);
        if (settings != null)
            _context.UsersSettings.Remove(settings);

        // 9. Delete user
        var user = await _context.Users.FindAsync(new object[] { userId }, ct);
        if (user != null)
            _context.Users.Remove(user);

        await _context.SaveChangesAsync(ct);
    }
}
