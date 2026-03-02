using DayTrace.Api.Middleware;
using DayTrace.Domain.Interfaces;
using DayTrace.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace DayTrace.Api.Controllers;

[ApiController]
[Route("admin/subscriptions")]
public class AdminSubscriptionsController : ControllerBase
{
    private const int DefaultListLimit = 20;
    private const int MaxListLimit = 100;

    private readonly SubscriptionService _subscriptionService;
    private readonly ISubscriptionRepository _subscriptionRepo;
    private readonly IStarPaymentRepository _starPaymentRepo;
    private readonly IAdminAuditService _adminAuditService;

    public AdminSubscriptionsController(
        SubscriptionService subscriptionService,
        ISubscriptionRepository subscriptionRepo,
        IStarPaymentRepository starPaymentRepo,
        IAdminAuditService adminAuditService)
    {
        _subscriptionService = subscriptionService;
        _subscriptionRepo = subscriptionRepo;
        _starPaymentRepo = starPaymentRepo;
        _adminAuditService = adminAuditService;
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int limit = DefaultListLimit,
        [FromQuery] int offset = 0,
        [FromQuery] string? status = null)
    {
        var admin = HttpContext.GetAdminUser();
        if (admin == null)
            return Unauthorized(new { error = "unauthorized" });

        limit = Math.Clamp(limit, 1, MaxListLimit);
        offset = Math.Max(offset, 0);

        var (items, total) = await _subscriptionRepo.GetAllAsync(limit, offset, status);

        var responseItems = new List<object>(items.Count);
        foreach (var sub in items)
        {
            var statusResult = await _subscriptionService.GetStatusAsync(sub.UserId);
            responseItems.Add(new
            {
                user_id = sub.UserId,
                telegram_id = sub.User?.TelegramUserId,
                status = ToStatusString(statusResult.Status),
                trial_expires_at = sub.TrialExpiresAt,
                subscription_expires_at = sub.SubscriptionExpiresAt,
                is_exempt = sub.IsExempt,
                days_remaining = statusResult.DaysRemaining
            });
        }

        await _adminAuditService.LogSuccessAsync(admin.Id, "list_subscriptions", "subscription", null);

        return Ok(new
        {
            items = responseItems,
            total,
            limit,
            offset
        });
    }

    [HttpGet("{userId:long}")]
    public async Task<IActionResult> GetDetail(long userId)
    {
        var admin = HttpContext.GetAdminUser();
        if (admin == null)
            return Unauthorized(new { error = "unauthorized" });

        var sub = await _subscriptionRepo.GetByUserIdAsync(userId);
        if (sub == null)
            return NotFound(new { error = "not_found", message = "Subscription not found for this user" });

        var statusResult = await _subscriptionService.GetStatusAsync(userId);
        var payments = await _starPaymentRepo.GetByUserIdAsync(userId, 100, 0);

        await _adminAuditService.LogSuccessAsync(admin.Id, "get_subscription_detail", "subscription", userId.ToString());

        return Ok(new
        {
            user_id = sub.UserId,
            telegram_id = sub.User?.TelegramUserId,
            status = statusResult.Status.ToString().ToLowerInvariant(),
            trial_started_at = sub.TrialStartedAt,
            trial_expires_at = sub.TrialExpiresAt,
            subscription_expires_at = sub.SubscriptionExpiresAt,
            is_exempt = sub.IsExempt,
            days_remaining = statusResult.DaysRemaining,
            created_at = sub.CreatedAt,
            updated_at = sub.UpdatedAt,
            payment_history = payments.Select(p => new
            {
                id = p.Id,
                plan = p.Plan,
                stars_amount = p.StarsAmount,
                telegram_payment_charge_id = p.TelegramPaymentChargeId,
                created_at = p.CreatedAt
            })
        });
    }

    [HttpPost("{userId:long}/exempt")]
    public async Task<IActionResult> SetExempt(long userId)
    {
        var admin = HttpContext.GetAdminUser();
        if (admin == null)
            return Unauthorized(new { error = "unauthorized" });

        await _subscriptionService.ExemptAsync(userId);
        await _adminAuditService.LogSuccessAsync(admin.Id, "set_subscription_exempt", "subscription", userId.ToString());

        return Ok(new { success = true, message = "User marked as exempt" });
    }

    [HttpDelete("{userId:long}/exempt")]
    public async Task<IActionResult> RemoveExempt(long userId)
    {
        var admin = HttpContext.GetAdminUser();
        if (admin == null)
            return Unauthorized(new { error = "unauthorized" });

        await _subscriptionService.RemoveExemptAsync(userId);
        await _adminAuditService.LogSuccessAsync(admin.Id, "remove_subscription_exempt", "subscription", userId.ToString());

        return Ok(new { success = true, message = "Exempt status removed" });
    }

    private static string ToStatusString(Domain.Enums.SubscriptionStatus status) => status switch
    {
        Domain.Enums.SubscriptionStatus.NotStarted => "not_started",
        Domain.Enums.SubscriptionStatus.Trial => "trial",
        Domain.Enums.SubscriptionStatus.Active => "active",
        Domain.Enums.SubscriptionStatus.GracePeriod => "grace_period",
        Domain.Enums.SubscriptionStatus.Expired => "expired",
        Domain.Enums.SubscriptionStatus.Exempt => "exempt",
        _ => status.ToString().ToLowerInvariant()
    };

    [HttpPost("{userId:long}/reset-trial")]
    public async Task<IActionResult> ResetTrial(long userId)
    {
        var admin = HttpContext.GetAdminUser();
        if (admin == null)
            return Unauthorized(new { error = "unauthorized" });

        await _subscriptionService.ResetTrialAsync(userId);
        await _adminAuditService.LogSuccessAsync(admin.Id, "reset_subscription_trial", "subscription", userId.ToString());

        return Ok(new { success = true, message = "Trial has been reset" });
    }
}
