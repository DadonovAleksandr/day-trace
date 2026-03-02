using DayTrace.Domain.Services;

namespace DayTrace.Api.Middleware;

public class SubscriptionCheckMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly string[] SkippedPrefixes =
    [
        "/subscription", "/auth", "/health", "/bot/webhook",
        "/swagger", "/admin", "/privacy", "/wisdoms",
        "/assets", "/today", "/week", "/month", "/year", "/settings", "/info"
    ];

    public SubscriptionCheckMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, SubscriptionService subscriptionService)
    {
        var path = context.Request.Path.Value ?? "";

        if (SkippedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        if (!context.Items.TryGetValue("UserId", out var userIdObj) || userIdObj is not long userId)
        {
            await _next(context);
            return;
        }

        var statusResult = await subscriptionService.GetStatusAsync(userId);
        if (!statusResult.HasAccess)
        {
            context.Response.StatusCode = 402;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new { error = "subscription_expired" });
            return;
        }

        await _next(context);
    }
}
