namespace DayTrace.Domain.Entities;

public class Subscription
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime? TrialStartedAt { get; set; }
    public DateTime? TrialExpiresAt { get; set; }
    public DateTime? SubscriptionExpiresAt { get; set; }
    public bool IsExempt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
