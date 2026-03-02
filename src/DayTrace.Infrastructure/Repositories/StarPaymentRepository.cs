using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class StarPaymentRepository : IStarPaymentRepository
{
    private readonly DayTraceDbContext _context;

    public StarPaymentRepository(DayTraceDbContext context)
    {
        _context = context;
    }

    public async Task<StarPayment> CreateAsync(StarPayment payment)
    {
        _context.StarPayments.Add(payment);
        await _context.SaveChangesAsync();
        return payment;
    }

    public async Task<StarPayment?> GetByChargeIdAsync(string chargeId)
    {
        return await _context.StarPayments
            .FirstOrDefaultAsync(p => p.TelegramPaymentChargeId == chargeId);
    }

    public async Task<List<StarPayment>> GetByUserIdAsync(long userId, int limit, int offset)
    {
        return await _context.StarPayments
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToListAsync();
    }
}
