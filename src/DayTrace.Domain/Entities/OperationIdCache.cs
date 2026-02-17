namespace DayTrace.Domain.Entities;

public class OperationIdCache
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Route { get; set; } = string.Empty;
    public string ClientOperationId { get; set; } = string.Empty;
    public string? ResponseHash { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}
