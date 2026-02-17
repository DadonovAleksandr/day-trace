namespace DayTrace.Domain.Entities;

public class AdminSession
{
    public long Id { get; set; }
    public long AdminUserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public AdminUser? AdminUser { get; set; }
}
