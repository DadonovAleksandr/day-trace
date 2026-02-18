using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Repositories;

public class PromptDeliveryRepository : IPromptDeliveryRepository
{
    private readonly DayTraceDbContext _context;

    public PromptDeliveryRepository(DayTraceDbContext context)
    {
        _context = context;
    }

    public async Task<PromptDelivery> CreateAsync(PromptDelivery delivery, CancellationToken ct = default)
    {
        _context.PromptDeliveries.Add(delivery);
        await _context.SaveChangesAsync(ct);
        return delivery;
    }

    public async Task<PromptDelivery?> GetByPromptIdAsync(string promptId, CancellationToken ct = default)
    {
        return await _context.PromptDeliveries
            .FirstOrDefaultAsync(p => p.PromptId == promptId, ct);
    }
}
