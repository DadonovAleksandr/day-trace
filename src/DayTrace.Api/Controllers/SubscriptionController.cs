using DayTrace.Api.Middleware;
using DayTrace.Domain.Constants;
using DayTrace.Domain.Services;
using Microsoft.AspNetCore.Mvc;
using Telegram.Bot;
using Telegram.Bot.Types.Payments;

namespace DayTrace.Api.Controllers;

[ApiController]
[Route("subscription")]
public class SubscriptionController : ControllerBase
{
    private readonly SubscriptionService _subscriptionService;
    private readonly ITelegramBotClient _botClient;
    private readonly ILogger<SubscriptionController> _logger;

    public SubscriptionController(
        SubscriptionService subscriptionService,
        ITelegramBotClient botClient,
        ILogger<SubscriptionController> logger)
    {
        _subscriptionService = subscriptionService;
        _botClient = botClient;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetStatus()
    {
        var userId = HttpContext.GetUserId();
        var result = await _subscriptionService.GetStatusAsync(userId);

        return Ok(new
        {
            status = ToStatusString(result.Status),
            trial_expires_at = result.TrialExpiresAt,
            subscription_expires_at = result.SubscriptionExpiresAt,
            days_remaining = result.DaysRemaining,
            is_exempt = result.IsExempt,
            has_access = result.HasAccess
        });
    }

    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout([FromBody] CheckoutRequest request)
    {
        var userId = HttpContext.GetUserId();

        var plan = request.Plan?.Trim().ToLowerInvariant();
        if (plan is not ("monthly" or "annual"))
            return BadRequest(new { error = "validation_error", message = "plan must be 'monthly' or 'annual'" });

        var (title, description, amount) = plan switch
        {
            "annual" => ("DayTrace Premium", "Подписка на 1 год (365 дней)", SubscriptionPlans.AnnualStars),
            _ => ("DayTrace Premium", "Подписка на 1 месяц (30 дней)", SubscriptionPlans.MonthlyStars)
        };

        var payload = $"sub_{plan}_{userId}_{DateTime.UtcNow:yyyyMMddHHmmss}";

        var invoiceLink = await _botClient.CreateInvoiceLink(
            title: title,
            description: description,
            payload: payload,
            currency: "XTR",
            prices: [new LabeledPrice(description, amount)]);

        _logger.LogInformation("Created invoice link for user {UserId}, plan={Plan}", userId, plan);

        return Ok(new { invoice_link = invoiceLink });
    }

    public class CheckoutRequest
    {
        public string? Plan { get; set; }
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
}
