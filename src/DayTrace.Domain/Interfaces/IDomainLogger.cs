namespace DayTrace.Domain.Interfaces;

/// <summary>
/// Domain-level logging abstraction for key business events.
/// </summary>
public interface IDomainLogger
{
    // General logging
    void Info(string message, params object[] args);
    void Warn(string message, params object[] args);
    void Error(string message, params object[] args);
    void Error(Exception ex, string message, params object[] args);

    // Domain-specific logging
    void LogReminderSendAttempt(long userId, string deliveryType, string status, string? error = null);
    void LogPeriodJobStart(string jobId, long userId, string periodType, DateOnly periodStart, DateOnly periodEnd);
    void LogPeriodJobResult(string jobId, string status, int? eventCount = null);
    void LogPeriodJobFailure(string jobId, string error, int attemptCount);
}
