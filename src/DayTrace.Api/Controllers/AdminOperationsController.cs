using DayTrace.Api.Middleware;
using DayTrace.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DayTrace.Api.Controllers;

/// <summary>
/// Admin operations endpoints: period jobs and delivery attempts monitoring.
/// Per US-057 / FR-11.
/// Requires operator role minimum.
/// </summary>
[ApiController]
[Route("admin")]
public class AdminOperationsController : ControllerBase
{
    private readonly IPeriodJobRepository _periodJobRepo;
    private readonly IDeliveryAttemptRepository _deliveryAttemptRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly ILogger<AdminOperationsController> _logger;

    public AdminOperationsController(
        IPeriodJobRepository periodJobRepo,
        IDeliveryAttemptRepository deliveryAttemptRepo,
        IAuditLogRepository auditLogRepo,
        ILogger<AdminOperationsController> logger)
    {
        _periodJobRepo = periodJobRepo;
        _deliveryAttemptRepo = deliveryAttemptRepo;
        _auditLogRepo = auditLogRepo;
        _logger = logger;
    }

    /// <summary>
    /// GET /admin/period-jobs — Period jobs list with filtering and pagination.
    /// Shows status, attempts, timing, errors. Stuck job indicators (running > 5 min).
    /// </summary>
    [HttpGet("period-jobs")]
    public async Task<IActionResult> ListPeriodJobs(
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        [FromQuery] string? status = null,
        [FromQuery] long? user_id = null)
    {
        var admin = HttpContext.GetAdminUser();
        if (admin == null)
            return Unauthorized(new { error = "unauthorized" });

        var jobs = await _periodJobRepo.AdminListAsync(limit, offset, status, user_id);
        var total = await _periodJobRepo.AdminCountAsync(status, user_id);

        await LogAudit(admin.Id, "list_period_jobs", "period_job", null);

        var stuckThreshold = DateTime.UtcNow.AddMinutes(-5);

        return Ok(new
        {
            items = jobs.Select(j => new
            {
                id = j.Id,
                idempotency_key = j.IdempotencyKey,
                user_id = j.UserId,
                period_type = j.PeriodType,
                period_start = j.PeriodStart,
                period_end = j.PeriodEnd,
                run_number = j.RunNumber,
                status = j.Status,
                attempt_count = j.AttemptCount,
                lease_id = j.LeaseId,
                target_summary_version = j.TargetSummaryVersion,
                started_at = j.StartedAt,
                finished_at = j.FinishedAt,
                error = j.Error,
                reconciled_at = j.ReconciledAt,
                recovery_source = j.RecoverySource,
                created_at = j.CreatedAt,
                is_stuck = j.Status == "running" && j.StartedAt.HasValue && j.StartedAt.Value < stuckThreshold
            }),
            total,
            limit,
            offset
        });
    }

    /// <summary>
    /// GET /admin/delivery-attempts — Delivery attempts with filtering and pagination.
    /// Shows status, retry count, scheduled vs actual times.
    /// </summary>
    [HttpGet("delivery-attempts")]
    public async Task<IActionResult> ListDeliveryAttempts(
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        [FromQuery] string? status = null,
        [FromQuery] long? user_id = null,
        [FromQuery] string? delivery_type = null)
    {
        var admin = HttpContext.GetAdminUser();
        if (admin == null)
            return Unauthorized(new { error = "unauthorized" });

        var attempts = await _deliveryAttemptRepo.AdminListAsync(limit, offset, status, user_id, delivery_type);
        var total = await _deliveryAttemptRepo.AdminCountAsync(status, user_id, delivery_type);

        await LogAudit(admin.Id, "list_delivery_attempts", "delivery_attempt", null);

        return Ok(new
        {
            items = attempts.Select(a => new
            {
                id = a.Id,
                user_id = a.UserId,
                delivery_type = a.DeliveryType,
                reference_id = a.ReferenceId,
                attempt_number = a.AttemptNumber,
                status = a.Status,
                error_message = a.ErrorMessage,
                telegram_message_id = a.TelegramMessageId,
                scheduled_at = a.ScheduledAt,
                sent_at = a.SentAt,
                created_at = a.CreatedAt
            }),
            total,
            limit,
            offset
        });
    }

    private async Task LogAudit(long adminId, string action, string? targetType, string? targetId)
    {
        await _auditLogRepo.CreateAsync(new Domain.Entities.AuditLog
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
