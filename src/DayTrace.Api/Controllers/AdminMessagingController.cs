using DayTrace.Api.Middleware;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;

namespace DayTrace.Api.Controllers;

[ApiController]
[Route("admin/messaging")]
public class AdminMessagingController : ControllerBase
{
    private readonly IUserRepository _userRepo;
    private readonly IDeliveryAttemptRepository _deliveryAttemptRepo;
    private readonly IAdminAuditService _adminAuditService;
    private readonly ILogger<AdminMessagingController> _logger;

    public AdminMessagingController(
        IUserRepository userRepo,
        IDeliveryAttemptRepository deliveryAttemptRepo,
        IAdminAuditService adminAuditService,
        ILogger<AdminMessagingController> logger)
    {
        _userRepo = userRepo;
        _deliveryAttemptRepo = deliveryAttemptRepo;
        _adminAuditService = adminAuditService;
        _logger = logger;
    }

    /// <summary>
    /// POST /admin/messaging/broadcast — Send message to active users or active users with reminders.
    /// </summary>
    [HttpPost("broadcast")]
    public async Task<IActionResult> Broadcast([FromBody] AdminBroadcastRequest? request)
    {
        var admin = HttpContext.GetAdminUser();
        if (admin == null)
            return Unauthorized(new { error = "unauthorized" });

        var role = HttpContext.GetAdminRole();
        if (role.Equals("analyst", StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, new { error = "forbidden", message = "Analyst role cannot send broadcasts" });

        var text = request?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text))
            return BadRequest(new { error = "invalid_request", message = "text is required" });

        var audience = request?.Audience?.Trim().ToLowerInvariant();
        if (audience is not ("active" or "reminders"))
            return BadRequest(new { error = "invalid_request", message = "audience must be 'active' or 'reminders'" });

        var botClient = HttpContext.RequestServices.GetService<ITelegramBotClient>();
        if (botClient == null)
            return StatusCode(503, new { error = "service_unavailable", message = "Telegram bot client is not configured" });

        var ct = HttpContext.RequestAborted;
        var total = 0;
        var sent = 0;
        var failed = 0;
        var sampleFailedUserIds = new List<long>(capacity: 10);

        if (audience == "reminders")
        {
            var users = await _userRepo.GetActiveUsersWithRemindersAsync(ct);
            total = users.Count;

            foreach (var user in users)
            {
                var ok = await SendBroadcastToUserAsync(user, botClient, text, sampleFailedUserIds, ct);
                if (ok) sent++;
                else failed++;
            }
        }
        else
        {
            const int pageSize = 200;
            var offset = 0;

            while (!ct.IsCancellationRequested)
            {
                var users = await _userRepo.GetAllAsync(pageSize, offset, search: null, status: "active", ct: ct);
                if (users.Count == 0)
                    break;

                total += users.Count;

                foreach (var user in users)
                {
                    var ok = await SendBroadcastToUserAsync(user, botClient, text, sampleFailedUserIds, ct);
                    if (ok) sent++;
                    else failed++;
                }

                if (users.Count < pageSize)
                    break;

                offset += users.Count;
            }
        }

        await _adminAuditService.LogSuccessAsync(admin.Id, "broadcast_message", "broadcast", audience);

        return Ok(new
        {
            total,
            sent,
            failed,
            audience,
            sample_failed_user_ids = sampleFailedUserIds
        });
    }

    private async Task<bool> SendBroadcastToUserAsync(
        User user,
        ITelegramBotClient botClient,
        string text,
        List<long> sampleFailedUserIds,
        CancellationToken ct)
    {
        var delivery = await AdminTelegramDeliveryHelper.SendAndLogAsync(
            _deliveryAttemptRepo,
            botClient,
            user.Id,
            user.TelegramUserId,
            deliveryType: "admin_broadcast",
            referenceId: null,
            text: text,
            ct: ct);

        if (delivery.IsSuccess)
            return true;

        if (sampleFailedUserIds.Count < 10)
            sampleFailedUserIds.Add(user.Id);

        _logger.LogWarning(
            "Admin broadcast delivery failed: user_id={UserId}, delivery_attempt_id={DeliveryAttemptId}, status={Status}, error={Error}",
            user.Id, delivery.Attempt.Id, delivery.Attempt.Status, delivery.ErrorMessage);

        return false;
    }

    public sealed class AdminBroadcastRequest
    {
        public string? Text { get; set; }
        public string? Audience { get; set; }
    }
}
