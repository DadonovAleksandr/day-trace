namespace DayTrace.Domain.Entities;

public class PromptDelivery
{
    public long Id { get; set; }
    public string PromptId { get; set; } = string.Empty;
    public long UserId { get; set; }
    public string PeriodType { get; set; } = string.Empty;
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public DateTime SentAt { get; set; }
    public string Channel { get; set; } = "auto"; // auto, manual
    public string Status { get; set; } = "sent";

    public User? User { get; set; }
}
