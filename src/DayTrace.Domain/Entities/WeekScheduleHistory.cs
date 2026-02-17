namespace DayTrace.Domain.Entities;

public class WeekScheduleHistory
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string WeekEnd { get; set; } = "Sunday";
    public DateOnly EffectiveFromLocalDate { get; set; }
    public DateOnly? TransitionStart { get; set; }
    public DateOnly? TransitionEnd { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}
