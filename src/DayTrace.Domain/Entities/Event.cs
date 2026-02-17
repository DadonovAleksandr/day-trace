namespace DayTrace.Domain.Entities;

public class Event
{
    public Guid Id { get; set; }
    public long UserId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateOnly LocalDate { get; set; }
    public int Importance { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    public User? User { get; set; }
}
