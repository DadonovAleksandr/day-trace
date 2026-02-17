namespace DayTrace.Domain.Entities;

public class PeriodJob
{
    public long Id { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public long UserId { get; set; }
    public string PeriodType { get; set; } = string.Empty;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public int RunNumber { get; set; }
    public string Status { get; set; } = "pending"; // pending, running, retried, failed, success, superseded
    public int AttemptCount { get; set; }
    public Guid? LeaseId { get; set; }
    public int TargetSummaryVersion { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? Error { get; set; }
    public DateTime? ReconciledAt { get; set; }
    public string? RecoverySource { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}
