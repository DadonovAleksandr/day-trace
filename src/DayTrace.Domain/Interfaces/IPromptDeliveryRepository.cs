using DayTrace.Domain.Entities;

namespace DayTrace.Domain.Interfaces;

public interface IPromptDeliveryRepository
{
    /// <summary>
    /// Creates a new prompt delivery record.
    /// </summary>
    Task<PromptDelivery> CreateAsync(PromptDelivery delivery, CancellationToken ct = default);

    /// <summary>
    /// Gets a prompt delivery by its prompt_id (idempotency key).
    /// </summary>
    Task<PromptDelivery?> GetByPromptIdAsync(string promptId, CancellationToken ct = default);
}
