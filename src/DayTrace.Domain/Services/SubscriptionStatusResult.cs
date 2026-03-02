using DayTrace.Domain.Enums;

namespace DayTrace.Domain.Services;

public record SubscriptionStatusResult(
    SubscriptionStatus Status,
    DateTime? TrialExpiresAt,
    DateTime? SubscriptionExpiresAt,
    int? DaysRemaining,
    bool IsExempt
)
{
    public bool HasAccess => Status is SubscriptionStatus.NotStarted
        or SubscriptionStatus.Trial
        or SubscriptionStatus.Active
        or SubscriptionStatus.GracePeriod
        or SubscriptionStatus.Exempt;
}
