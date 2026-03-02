namespace DayTrace.Domain.Interfaces;

public interface ITransactionExecutor
{
    Task<T> ExecuteAsync<T>(Func<Task<T>> operation);
}
