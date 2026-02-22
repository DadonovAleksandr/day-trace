namespace DayTrace.Domain.Entities;

public class DayRating
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public DateOnly LocalDate { get; set; }
    public int Rating { get; set; } // 1-5
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public User? User { get; set; }
}
