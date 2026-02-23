using DayTrace.Domain.Entities;

namespace DayTrace.Domain.Interfaces;

public interface IAdminBroadcastCampaignRepository
{
    Task<AdminBroadcastCampaign> CreateAsync(AdminBroadcastCampaign campaign, CancellationToken ct = default);
    Task UpdateAsync(AdminBroadcastCampaign campaign, CancellationToken ct = default);
    Task<AdminBroadcastCampaign?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<List<AdminBroadcastCampaign>> AdminListAsync(int limit, int offset, string? audience = null, CancellationToken ct = default);
    Task<int> AdminCountAsync(string? audience = null, CancellationToken ct = default);
}
