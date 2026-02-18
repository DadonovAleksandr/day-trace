using DayTrace.Api.Middleware;
using DayTrace.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DayTrace.Api.Controllers;

/// <summary>
/// Admin audit log endpoint.
/// Per US-058 / FR-11, NFR-4.
/// Only admin role can view full audit log.
/// </summary>
[ApiController]
[Route("admin/audit-logs")]
public class AdminAuditController : ControllerBase
{
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly ILogger<AdminAuditController> _logger;

    public AdminAuditController(IAuditLogRepository auditLogRepo, ILogger<AdminAuditController> logger)
    {
        _auditLogRepo = auditLogRepo;
        _logger = logger;
    }

    /// <summary>
    /// GET /admin/audit-logs — Audit log list with filtering and pagination.
    /// Filterable by actor, action type, date range.
    /// Includes login/logout events.
    /// Only admin role can view.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListAuditLogs(
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        [FromQuery] string? actor_type = null,
        [FromQuery] string? action = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null)
    {
        var admin = HttpContext.GetAdminUser();
        if (admin == null)
            return Unauthorized(new { error = "unauthorized" });

        // Only admin role can view audit logs (enforced by middleware, but double-check)
        if (!HttpContext.IsAdminRole())
            return StatusCode(403, new { error = "forbidden", message = "Only admin role can view audit logs" });

        var logs = await _auditLogRepo.GetAllAsync(limit, offset, actor_type, action, from, to);
        var total = await _auditLogRepo.CountAsync(actor_type, action, from, to);

        return Ok(new
        {
            items = logs.Select(l => new
            {
                id = l.Id,
                actor_type = l.ActorType,
                actor_id = l.ActorId,
                action = l.Action,
                target_type = l.TargetType,
                target_id = l.TargetId,
                payload = l.Payload,
                outcome = l.Outcome,
                created_at = l.CreatedAt
            }),
            total,
            limit,
            offset
        });
    }
}
