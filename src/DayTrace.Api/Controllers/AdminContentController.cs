using DayTrace.Api.Middleware;
using DayTrace.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DayTrace.Api.Controllers;

/// <summary>
/// Admin content management: browse events, summaries and feedback with role-based PII access.
/// Per US-056 / FR-11, NFR-4.
/// </summary>
[ApiController]
[Route("admin")]
public class AdminContentController : ControllerBase
{
    private readonly IEventRepository _eventRepo;
    private readonly ISummaryRepository _summaryRepo;
    private readonly IUserFeedbackRepository _feedbackRepo;
    private readonly IAuditLogRepository _auditLogRepo;
    private readonly ILogger<AdminContentController> _logger;

    public AdminContentController(
        IEventRepository eventRepo,
        ISummaryRepository summaryRepo,
        IUserFeedbackRepository feedbackRepo,
        IAuditLogRepository auditLogRepo,
        ILogger<AdminContentController> logger)
    {
        _eventRepo = eventRepo;
        _summaryRepo = summaryRepo;
        _feedbackRepo = feedbackRepo;
        _auditLogRepo = auditLogRepo;
        _logger = logger;
    }

    /// <summary>
    /// GET /admin/events — Events list, filterable by user, date range, importance.
    /// Analyst role: no access to event texts (redacted).
    /// </summary>
    [HttpGet("events")]
    public async Task<IActionResult> ListEvents(
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        [FromQuery] long? user_id = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] int? importance = null)
    {
        var admin = HttpContext.GetAdminUser();
        if (admin == null)
            return Unauthorized(new { error = "unauthorized" });

        var role = HttpContext.GetAdminRole();
        var isAnalyst = role.Equals("analyst", StringComparison.OrdinalIgnoreCase);

        // Analyst: no access to event texts
        if (isAnalyst)
            return StatusCode(403, new { error = "forbidden", message = "Analyst role cannot access event texts" });

        var events = await _eventRepo.AdminListAsync(limit, offset, user_id, from, to, importance);
        var total = await _eventRepo.AdminCountAsync(user_id, from, to, importance);

        await LogAudit(admin.Id, "list_events", "event", null);

        return Ok(new
        {
            items = events.Select(e => new
            {
                id = e.Id,
                user_id = e.UserId,
                text = e.Text,
                local_date = e.LocalDate,
                importance = e.Importance,
                created_at = e.CreatedAt,
                updated_at = e.UpdatedAt,
            }),
            total,
            limit,
            offset
        });
    }

    /// <summary>
    /// GET /admin/summaries — Summaries list, filterable by user, period type, date range, status.
    /// Analyst: no access to event texts within content.
    /// </summary>
    [HttpGet("summaries")]
    public async Task<IActionResult> ListSummaries(
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        [FromQuery] long? user_id = null,
        [FromQuery] string? period_type = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? status = null)
    {
        var admin = HttpContext.GetAdminUser();
        if (admin == null)
            return Unauthorized(new { error = "unauthorized" });

        var role = HttpContext.GetAdminRole();
        var isAnalyst = role.Equals("analyst", StringComparison.OrdinalIgnoreCase);

        var summaries = await _summaryRepo.AdminListAsync(limit, offset, user_id, period_type, from, to, status);
        var total = await _summaryRepo.AdminCountAsync(user_id, period_type, from, to, status);

        await LogAudit(admin.Id, "list_summaries", "summary", null);

        return Ok(new
        {
            items = summaries.Select(s => new
            {
                id = s.Id,
                user_id = s.UserId,
                period_type = s.PeriodType,
                period_start = s.PeriodStart,
                period_end = s.PeriodEnd,
                status = s.Status,
                version = s.Version,
                // Analyst: redact content
                content = isAnalyst ? null : s.Content,
                last_generated_at = s.LastGeneratedAt,
            }),
            total,
            limit,
            offset
        });
    }

    /// <summary>
    /// GET /admin/feedback — Feedback list, filterable by user, status, date range.
    /// Analyst role: no access to feedback texts.
    /// </summary>
    [HttpGet("feedback")]
    public async Task<IActionResult> ListFeedback(
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        [FromQuery] long? user_id = null,
        [FromQuery] string? status = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null)
    {
        var admin = HttpContext.GetAdminUser();
        if (admin == null)
            return Unauthorized(new { error = "unauthorized" });

        var role = HttpContext.GetAdminRole();
        var isAnalyst = role.Equals("analyst", StringComparison.OrdinalIgnoreCase);

        if (isAnalyst)
            return StatusCode(403, new { error = "forbidden", message = "Analyst role cannot access feedback texts" });

        var feedbacks = await _feedbackRepo.AdminListAsync(limit, offset, user_id, status, from, to);
        var total = await _feedbackRepo.AdminCountAsync(user_id, status, from, to);

        await LogAudit(admin.Id, "list_feedback", "feedback", null);

        return Ok(new
        {
            items = feedbacks.Select(f => new
            {
                id = f.Id,
                user_id = f.UserId,
                telegram_user_id = f.User?.TelegramUserId,
                text = f.Text,
                status = f.Status,
                created_at = f.CreatedAt,
                read_at = f.ReadAt,
            }),
            total,
            limit,
            offset
        });
    }

    /// <summary>
    /// PATCH /admin/feedback/{id}/read — Mark feedback as read.
    /// </summary>
    [HttpPatch("feedback/{id}/read")]
    public async Task<IActionResult> MarkFeedbackRead(long id)
    {
        var admin = HttpContext.GetAdminUser();
        if (admin == null)
            return Unauthorized(new { error = "unauthorized" });

        var role = HttpContext.GetAdminRole();
        var isAnalyst = role.Equals("analyst", StringComparison.OrdinalIgnoreCase);

        if (isAnalyst)
            return StatusCode(403, new { error = "forbidden", message = "Analyst role cannot manage feedback" });

        var feedback = await _feedbackRepo.GetByIdAsync(id);
        if (feedback == null)
            return NotFound(new { error = "not_found" });

        if (feedback.Status == "read")
            return Ok(new { id = feedback.Id, status = feedback.Status, read_at = feedback.ReadAt });

        feedback.Status = "read";
        feedback.ReadAt = DateTime.UtcNow;
        await _feedbackRepo.UpdateAsync(feedback);

        await LogAudit(admin.Id, "mark_feedback_read", "feedback", id.ToString());

        return Ok(new { id = feedback.Id, status = feedback.Status, read_at = feedback.ReadAt });
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
