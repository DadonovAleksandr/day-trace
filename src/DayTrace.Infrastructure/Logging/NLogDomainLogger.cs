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

}
