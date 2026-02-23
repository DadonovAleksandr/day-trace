namespace DayTrace.Domain.Entities;

public class Summary
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string PeriodType { get; set; } = string.Empty; // weekly, monthly, yearly
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public string Status { get; set; } = "generated"; // generated
    public int Version { get; set; } = 1;
    public string? Content { get; set; } // JSONB
    public Guid[]? SourceEventIds { get; set; }
    public DateTime? LastGeneratedAt { get; set; }
    public Guid? HighlightEventId { get; set; }

    public User? User { get; set; }
    public Event? HighlightEvent { get; set; }
}
