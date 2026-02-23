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
        Secure = request.IsHttps,
        Path = CookiePath,
        Expires = DateTimeOffset.UtcNow.Add(CookieLifetime)
    };

    private static CookieOptions BuildDeleteCookieOptions(HttpRequest request) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        Secure = request.IsHttps,
        Path = CookiePath
    };
}
