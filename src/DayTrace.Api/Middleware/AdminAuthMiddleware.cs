using DayTrace.Domain.Entities;
using DayTrace.Domain.Services;

namespace DayTrace.Api.Middleware;

/// <summary>
/// Admin RBAC middleware: validates admin session token and enforces role-based access.
/// Per US-052 / FR-11, FR-12.4, NFR-4.
/// Role hierarchy: admin > operator > analyst.
/// </summary>
public class AdminAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AdminAuthMiddleware> _logger;

    public AdminAuthMiddleware(RequestDelegate next, ILogger<AdminAuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Only apply to /admin/ paths (except /admin/auth/login which is anonymous)
        if (!path.StartsWith("/admin/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Allow /admin/auth/login without token
        if (path.StartsWith("/admin/auth/login", StringComparison.OrdinalIgnoreCase))
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

        // Validate admin session
        var adminAuthService = context.RequestServices.GetRequiredService<AdminAuthService>();
        var session = await adminAuthService.ValidateSessionAsync(token);

        if (session?.AdminUser == null)
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsJsonAsync(new { error = "unauthorized", message = "Invalid or expired admin session" });
            return;
        }

        var admin = session.AdminUser;

        // Check role-based access
        var requiredRole = GetRequiredRole(path, context.Request.Method);
        if (requiredRole != null && !HasSufficientRole(admin.Role, requiredRole))
        {
            context.Response.StatusCode = 403;
            await context.Response.WriteAsJsonAsync(new { error = "forbidden", message = $"Insufficient role. Required: {requiredRole}" });
            return;
        }

        // Set admin context
        context.Items["AdminUser"] = admin;
        context.Items["AdminSessionId"] = session.Id;
        context.Items["AdminRole"] = admin.Role;

        await _next(context);
    }

    /// <summary>
    /// Determines the minimum required role for a path.
    /// admin > operator > analyst
    /// </summary>
    private static string? GetRequiredRole(string path, string method)
    {
        // Metrics/dashboard — analyst minimum
        if (path.StartsWith("/admin/metrics", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/admin/dashboard", StringComparison.OrdinalIgnoreCase))
            return "analyst";

        // Audit logs — admin only
        if (path.StartsWith("/admin/audit", StringComparison.OrdinalIgnoreCase))
            return "admin";

        // Users, events, summaries, period-jobs, delivery-attempts: operator minimum
        // (operator = read-only, admin = full access)
        return "operator";
    }

    /// <summary>
    /// Checks if actualRole >= requiredRole in hierarchy: admin > operator > analyst.
    /// </summary>
    private static bool HasSufficientRole(string actualRole, string requiredRole)
    {
        var roleLevel = GetRoleLevel(actualRole);
        var requiredLevel = GetRoleLevel(requiredRole);
        return roleLevel >= requiredLevel;
    }

    private static int GetRoleLevel(string role) => role.ToLowerInvariant() switch
    {
        "admin" => 3,
        "operator" => 2,
        "analyst" => 1,
        _ => 0
    };
}

/// <summary>
/// Extension for accessing admin context from HttpContext.
/// </summary>
public static class HttpContextAdminExtensions
{
    public static AdminUser? GetAdminUser(this HttpContext context)
    {
        return context.Items.TryGetValue("AdminUser", out var admin) ? admin as AdminUser : null;
    }

    public static string GetAdminRole(this HttpContext context)
    {
        return context.Items.TryGetValue("AdminRole", out var role) && role is string r ? r : "";
    }

    public static bool IsAdminRole(this HttpContext context)
    {
        return context.GetAdminRole().Equals("admin", StringComparison.OrdinalIgnoreCase);
    }
}
