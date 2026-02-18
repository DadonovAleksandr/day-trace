namespace DayTrace.Domain.Interfaces;

/// <summary>
/// Repository for computing dashboard metrics.
/// Per US-054 / FR-11, METRICS.md formulas.
/// </summary>
public interface IMetricsRepository
{
    /// <summary>DAU: distinct user_ids with events created on target day.</summary>
    Task<int> GetDailyActiveUsersAsync(DateTime targetDay);

    /// <summary>WAU: distinct user_ids with events in last 7 days (sliding window).</summary>
    Task<int> GetWeeklyActiveUsersAsync(DateTime asOf);

    /// <summary>MAU: distinct user_ids with events in last 30 days (sliding window).</summary>
    Task<int> GetMonthlyActiveUsersAsync(DateTime asOf);

    /// <summary>
    /// Reminder→Event conversion rate:
    /// Number of users who created an event within 24h after a reminder / total reminders sent.
    /// </summary>
    Task<(int converted, int total)> GetReminderConversionAsync(DateTime asOf);

    /// <summary>
    /// Prompt→Summary conversion rate:
    /// Number of successful summaries within 48h of prompt / total prompts.
    /// </summary>
    Task<(int converted, int total)> GetPromptConversionAsync(DateTime asOf);
}
