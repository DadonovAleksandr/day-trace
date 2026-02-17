namespace DayTrace.Domain.Entities;

public class PeriodRunCounter
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string PeriodType { get; set; } = string.Empty;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public int LastRunNumber { get; set; } = 1;

    public User? User { get; set; }
}
