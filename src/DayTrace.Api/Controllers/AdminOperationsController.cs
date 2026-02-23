using DayTrace.Api.Middleware;
using DayTrace.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DayTrace.Api.Controllers;

/// <summary>
/// Admin operations endpoints: delivery attempts monitoring.
/// Per US-057 / FR-11.
/// Requires operator role minimum.
/// </summary>
[ApiController]
[Route("admin")]
public class AdminOperationsController : ControllerBase
{
    private readonly IDeliveryAttemptRepository _deliveryAttemptRepo;
    private readonly IAdminAuditService _adminAuditService;
    private readonly ILogger<AdminOperationsController> _logger;

    public AdminOperationsController(
        IDeliveryAttemptRepository deliveryAttemptRepo,
        IAdminAuditService adminAuditService,
        ILogger<AdminOperationsController> logger)
    {
        _deliveryAttemptRepo = deliveryAttemptRepo;
        _adminAuditService = adminAuditService;
        _logger = logger;
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

        await _adminAuditService.LogSuccessAsync(admin.Id, "list_delivery_attempts", "delivery_attempt", null);

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

}
