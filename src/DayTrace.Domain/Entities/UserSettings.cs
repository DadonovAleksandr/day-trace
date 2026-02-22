namespace DayTrace.Domain.Entities;

public class UserSettings
{
    public long UserId { get; set; }
    public string Timezone { get; set; } = "UTC";
    public TimeOnly ReminderTime { get; set; } = new(21, 0);
    public bool ReminderEnabled { get; set; } = true;
    public string WeekEnd { get; set; } = "Sunday";
    public bool ShowWisdom { get; set; } = true;
    public int WisdomDuration { get; set; } = 10;

    public User? User { get; set; }
}
