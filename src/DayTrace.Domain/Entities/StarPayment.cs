namespace DayTrace.Domain.Entities;

public class StarPayment
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public string Plan { get; set; } = null!;
    public int StarsAmount { get; set; }
    public string TelegramPaymentChargeId { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
}
