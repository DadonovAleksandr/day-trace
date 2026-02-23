namespace DayTrace.Domain.Interfaces;

public interface IAdminAuditService
{
    Task LogSuccessAsync(long adminId, string action, string? targetType, string? targetId);
}
