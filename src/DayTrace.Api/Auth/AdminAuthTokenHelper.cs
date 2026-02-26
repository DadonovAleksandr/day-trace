namespace DayTrace.Api.Auth;

public static class AdminAuthTokenHelper
{
    public const string CookieName = "daytrace_admin_session";
    // Use root path so the cookie is sent both to /admin/* and reverse-proxied /api/admin/*.
    public const string CookiePath = "/";
    public static readonly TimeSpan CookieLifetime = TimeSpan.FromHours(8);

    public static string? ExtractToken(HttpRequest request)
    {
        if (request.Cookies.TryGetValue(CookieName, out var cookieToken) &&
            !string.IsNullOrWhiteSpace(cookieToken))
        {
            return cookieToken.Trim();
        }

        var authHeader = request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader) ||
            !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var bearerToken = authHeader["Bearer ".Length..].Trim();
        return string.IsNullOrWhiteSpace(bearerToken) ? null : bearerToken;
    }

    public static void SetCookie(HttpContext context, string token)
    {
        context.Response.Cookies.Append(CookieName, token, BuildCookieOptions(context.Request));
    }

    public static void DeleteCookie(HttpContext context)
    {
        context.Response.Cookies.Delete(CookieName, BuildDeleteCookieOptions(context.Request));
    }

    private static CookieOptions BuildCookieOptions(HttpRequest request) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        // Use Secure=true when the request is HTTPS, or when behind a reverse proxy
        // that terminates TLS (X-Forwarded-Proto: https). This ensures the cookie is
        // always sent securely in production even when the app itself runs over HTTP
        // behind a load balancer or nginx.
        Secure = IsSecureRequest(request),
        Path = CookiePath,
        Expires = DateTimeOffset.UtcNow.Add(CookieLifetime)
    };

    private static CookieOptions BuildDeleteCookieOptions(HttpRequest request) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = IsSecureRequest(request),
        Path = CookiePath
    };

    /// <summary>
    /// Returns true if the request is HTTPS directly or forwarded as HTTPS by a reverse proxy.
    /// Prevents the admin session cookie from being sent over plain HTTP in production.
    /// </summary>
    private static bool IsSecureRequest(HttpRequest request)
    {
        if (request.IsHttps)
            return true;

        // Check X-Forwarded-Proto set by nginx / load balancers
        var forwardedProto = request.Headers["X-Forwarded-Proto"].FirstOrDefault();
        return string.Equals(forwardedProto, "https", StringComparison.OrdinalIgnoreCase);
    }
}
