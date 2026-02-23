using DayTrace.Domain.Entities;

namespace DayTrace.Domain.Interfaces;

public interface IUserFeedbackRepository
{
    Task<UserFeedback> CreateAsync(UserFeedback feedback, CancellationToken ct = default);
    Task<UserFeedback?> GetByIdAsync(long id, CancellationToken ct = default);
    Task UpdateAsync(UserFeedback feedback, CancellationToken ct = default);

    /// <summary>Admin: list feedback with filtering and pagination. Includes User navigation property.</summary>
    Task<List<UserFeedback>> AdminListAsync(int limit, int offset, long? userId = null, string? status = null, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default);

    /// <summary>Admin: count feedback with filtering.</summary>
    Task<int> AdminCountAsync(long? userId = null, string? status = null, DateOnly? from = null, DateOnly? to = null, CancellationToken ct = default);
}
