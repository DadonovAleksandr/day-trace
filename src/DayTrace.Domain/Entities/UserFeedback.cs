namespace DayTrace.Domain.Entities;

public class UserFeedback
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Status { get; set; } = "new";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }

    public User? User { get; set; }
}
