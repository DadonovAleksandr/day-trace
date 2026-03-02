using DayTrace.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace DayTrace.Infrastructure.Data;

public class EfCoreTransactionExecutor : ITransactionExecutor
{
    private readonly DayTraceDbContext _dbContext;

    public EfCoreTransactionExecutor(DayTraceDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation)
    {
        // Reuse outer transaction if caller already has one.
        if (_dbContext.Database.CurrentTransaction != null)
        {
            return await operation();
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _dbContext.Database.BeginTransactionAsync();
            try
            {
                var result = await operation();
                await tx.CommitAsync();
                return result;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        });
    }
}
