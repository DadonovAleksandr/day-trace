namespace DayTrace.Domain.Constants;

public static class SubscriptionPlans
{
    public const int MonthlyStars = 100;
    public const int AnnualStars = 960;
    public static readonly TimeSpan MonthlyDuration = TimeSpan.FromDays(30);
    public static readonly TimeSpan AnnualDuration = TimeSpan.FromDays(365);
}
