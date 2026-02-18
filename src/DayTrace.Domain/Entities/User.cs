namespace DayTrace.Domain.Entities;

public class User
{
    public long Id { get; set; }
    public long TelegramUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "active";
    public DateTime? DeletedAt { get; set; }

    public UserSettings? Settings { get; set; }
}
