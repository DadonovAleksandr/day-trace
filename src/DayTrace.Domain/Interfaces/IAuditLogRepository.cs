namespace DayTrace.Domain.Interfaces;

using DayTrace.Domain.Entities;

public interface IAuditLogRepository
{
    Task<AuditLog> CreateAsync(AuditLog log);
    Task<List<AuditLog>> GetAllAsync(int limit, int offset, string? actorType = null, string? action = null, DateTime? from = null, DateTime? to = null);
    Task<int> CountAsync(string? actorType = null, string? action = null, DateTime? from = null, DateTime? to = null);
    Task<int> DeleteOlderThanAsync(DateTime cutoff, int batchSize = 1000);
}
