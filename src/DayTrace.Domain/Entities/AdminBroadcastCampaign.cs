namespace DayTrace.Domain.Entities;

public class AdminBroadcastCampaign
{
    public long Id { get; set; }
    public long CreatedByAdminUserId { get; set; }
    public string Audience { get; set; } = string.Empty; // active, reminders
    public string Text { get; set; } = string.Empty;
    public string Status { get; set; } = "queued"; // queued, processing, completed, partial_failed, failed
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public AdminUser? CreatedByAdminUser { get; set; }
}
