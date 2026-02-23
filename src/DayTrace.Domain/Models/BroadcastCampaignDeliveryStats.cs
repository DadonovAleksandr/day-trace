namespace DayTrace.Domain.Models;

public sealed class BroadcastCampaignDeliveryStats
{
    public long CampaignId { get; set; }
    public int Total { get; set; }
    public int Pending { get; set; }
    public int Sent { get; set; }
    public int Failed { get; set; }
    public int TerminalFailed { get; set; }
}
