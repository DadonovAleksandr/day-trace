namespace DayTrace.Domain.Entities;

/// <summary>
/// Replay protection cache for Telegram Mini App auth (FR-12.1).
/// Key = SHA256 of canonicalized init data.
/// </summary>
public class AuthReplayCache
{
    public long Id { get; set; }
    public string DataHash { get; set; } = string.Empty;
    public string SessionToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
