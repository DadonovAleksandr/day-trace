namespace DayTrace.Domain.Entities;

public class AuditLog
{
    public long Id { get; set; }
    public string ActorType { get; set; } = string.Empty; // admin, system
    public string? ActorId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? Payload { get; set; } // JSONB
    public string Outcome { get; set; } = "success"; // success, failure
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
