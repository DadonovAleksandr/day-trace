using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;

namespace DayTrace.Domain.Services;

public class AdminAuditService : IAdminAuditService
{
    private readonly IAuditLogRepository _auditLogRepository;

    public AdminAuditService(IAuditLogRepository auditLogRepository)
    {
        _auditLogRepository = auditLogRepository;
    }

    public async Task LogSuccessAsync(long adminId, string action, string? targetType, string? targetId)
    {
        await _auditLogRepository.CreateAsync(new AuditLog
        {
            ActorType = "admin",
            ActorId = adminId.ToString(),
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Outcome = "success",
            CreatedAt = DateTime.UtcNow
        });
    }
}
