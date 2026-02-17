namespace DayTrace.Domain.Entities;

public class DeliveryAttempt
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string DeliveryType { get; set; } = string.Empty; // reminder, summary_notification, soft_reminder
    public long? ReferenceId { get; set; }
    public int AttemptNumber { get; set; } = 1;
    public string Status { get; set; } = "pending"; // pending, sent, failed, terminal_failed
    public string? ErrorMessage { get; set; }
    public long? TelegramMessageId { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}
