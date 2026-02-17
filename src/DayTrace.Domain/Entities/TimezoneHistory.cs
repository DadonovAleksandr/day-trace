namespace DayTrace.Domain.Entities;

public class TimezoneHistory
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Timezone { get; set; } = "UTC";
    public DateTime EffectiveFrom { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}
