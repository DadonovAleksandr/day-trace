using DayTrace.Domain.Interfaces;
using NLog;

namespace DayTrace.Infrastructure.Logging;

public class NLogDomainLogger : IDomainLogger
{
    private static readonly ILogger Logger = LogManager.GetLogger("DayTrace.Domain");

    public void Info(string message, params object[] args) => Logger.Info(message, args);
    public void Warn(string message, params object[] args) => Logger.Warn(message, args);
    public void Error(string message, params object[] args) => Logger.Error(message, args);
    public void Error(Exception ex, string message, params object[] args) => Logger.Error(ex, message, args);

    public void LogReminderSendAttempt(long userId, string deliveryType, string status, string? error = null)
    {
        if (error != null)
        {
            Logger.Warn("Reminder send attempt: user_id={userId}, delivery_type={deliveryType}, status={status}, error={error}",
                userId, deliveryType, status, error);
        }
        else
        {
            Logger.Info("Reminder send attempt: user_id={userId}, delivery_type={deliveryType}, status={status}",
                userId, deliveryType, status);
        }
    }

    public void LogPeriodJobStart(string jobId, long userId, string periodType, DateOnly periodStart, DateOnly periodEnd)
    {
        Logger.Info("Period job started: job_id={jobId}, user_id={userId}, period_type={periodType}, period_start={periodStart}, period_end={periodEnd}",
            jobId, userId, periodType, periodStart, periodEnd);
    }

    public void LogPeriodJobResult(string jobId, string status, int? eventCount = null)
    {
        Logger.Info("Period job result: job_id={jobId}, status={status}, event_count={eventCount}",
            jobId, status, eventCount);
    }

    public void LogPeriodJobFailure(string jobId, string error, int attemptCount)
    {
        Logger.Error("Period job failed: job_id={jobId}, error={error}, attempt_count={attemptCount}",
            jobId, error, attemptCount);
    }
}
