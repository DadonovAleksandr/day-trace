using DayTrace.Domain.Interfaces;
using DayTrace.Domain.Services;

namespace DayTrace.Api.Middleware;

/// <summary>
/// Auth middleware: validates session token from Authorization: Bearer header.
/// Sets user context (user_id, timezone) for downstream handlers.
/// Per US-012 / FR-12.1 / FR-12.2.
/// </summary>
public class SessionAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SessionAuthMiddleware> _logger;

    // Paths that don't require authentication
    private static readonly HashSet<string> AnonymousPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/auth/telegram",
        "/health",
        "/bot/webhook",
        "/swagger",
        "/admin/",
    };

    public SessionAuthMiddleware(RequestDelegate next, ILogger<SessionAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Skip auth for anonymous paths
        if (IsAnonymousPath(path))
        {
            await _next(context);
            return;
        }

        // Extract Bearer token
        var authHeader = context.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "unauthorized", message = "Missing or invalid Authorization header" });
            return;
        }

        var token = authHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "unauthorized", message = "Empty token" });
            return;
        }

        // Validate token via DB
        var tokenHash = TelegramAuthService.ComputeSha256(token);
        var sessionRepo = context.RequestServices.GetRequiredService<ISessionRepository>();
        var session = await sessionRepo.GetByTokenHashAsync(tokenHash);

        if (session == null)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "unauthorized", message = "Invalid or expired session token" });
            return;
        }

        // Block inactive/deleted users
        if (session.User?.Status != "active")
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { error = "account_inactive", message = "Account is no longer active" });
            return;
        }

        // Sliding window renewal: extend expires_at by 24h on each valid request
        session.ExpiresAt = DateTime.UtcNow.AddHours(24);
        await sessionRepo.UpdateAsync(session);

        // Set user context for downstream handlers
        context.Items["UserId"] = session.UserId;
        context.Items["User"] = session.User;
        context.Items["Timezone"] = session.User?.Settings?.Timezone ?? "UTC";

        await _next(context);
    }

    private static bool IsAnonymousPath(string path)
    {
        foreach (var anonPath in AnonymousPaths)
        {
            if (path.StartsWith(anonPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Skip auth for static files served by miniapp SPA
        if (path == "/" || path.StartsWith("/assets/", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return true;

        // Skip auth for SPA routes (non-API paths handled by Vue Router)
        if (path.StartsWith("/today", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/week", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/month", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/year", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/settings", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}

/// <summary>
/// Extension methods for accessing authenticated user context from HttpContext.
/// </summary>
public static class HttpContextUserExtensions
{
    public static long GetUserId(this HttpContext context)
    {
        return context.Items.TryGetValue("UserId", out var userId) && userId is long id
            ? id
            : throw new UnauthorizedAccessException("User not authenticated");
    }

    public static string GetTimezone(this HttpContext context)
    {
        return context.Items.TryGetValue("Timezone", out var tz) && tz is string timezone
            ? timezone
            : "UTC";
    }
}
