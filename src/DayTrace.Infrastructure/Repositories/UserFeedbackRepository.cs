using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class UserFeedbackRepository : IUserFeedbackRepository
{
    private readonly DayTraceDbContext _context;

    public UserFeedbackRepository(DayTraceDbContext context)
    {
        _context = context;
    }

    public async Task<UserFeedback> CreateAsync(UserFeedback feedback, CancellationToken ct = default)
    {
        _context.UserFeedbacks.Add(feedback);
        await _context.SaveChangesAsync(ct);
        return feedback;
    }

    public async Task<UserFeedback?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        return await _context.UserFeedbacks
            .FirstOrDefaultAsync(f => f.Id == id, ct);
    }

    public async Task UpdateAsync(UserFeedback feedback, CancellationToken ct = default)
    {
        _context.UserFeedbacks.Update(feedback);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<List<UserFeedback>> AdminListAsync(int limit, int offset, long? userId = null, string? status = null, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        var query = _context.UserFeedbacks.Include(f => f.User).AsQueryable();
        if (userId.HasValue) query = query.Where(f => f.UserId == userId.Value);
        if (!string.IsNullOrEmpty(status)) query = query.Where(f => f.Status == status);
        if (from.HasValue) query = query.Where(f => DateOnly.FromDateTime(f.CreatedAt) >= from.Value);
        if (to.HasValue) query = query.Where(f => DateOnly.FromDateTime(f.CreatedAt) <= to.Value);

        return await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<int> AdminCountAsync(long? userId = null, string? status = null, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default)
    {
        var query = _context.UserFeedbacks.AsQueryable();
        if (userId.HasValue) query = query.Where(f => f.UserId == userId.Value);
        if (!string.IsNullOrEmpty(status)) query = query.Where(f => f.Status == status);
        if (from.HasValue) query = query.Where(f => DateOnly.FromDateTime(f.CreatedAt) >= from.Value);
        if (to.HasValue) query = query.Where(f => DateOnly.FromDateTime(f.CreatedAt) <= to.Value);

        return await query.CountAsync(ct);
    }
}
