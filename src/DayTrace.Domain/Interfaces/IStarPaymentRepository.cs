using DayTrace.Domain.Entities;

namespace DayTrace.Domain.Interfaces;

public interface IStarPaymentRepository
{
    Task<StarPayment> CreateAsync(StarPayment payment);
    Task<StarPayment?> GetByChargeIdAsync(string chargeId);
    Task<List<StarPayment>> GetByUserIdAsync(long userId, int limit, int offset);
}
