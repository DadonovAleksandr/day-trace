using DayTrace.Api.Auth;
using DayTrace.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace DayTrace.Api.Controllers;

[ApiController]
[Route("admin/auth")]
public class AdminAuthController : ControllerBase
{
    private readonly AdminAuthService _authService;
    private readonly ILogger<AdminAuthController> _logger;

    public AdminAuthController(AdminAuthService authService, ILogger<AdminAuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// POST /admin/auth/login — Authenticate admin user.
    /// Per US-051 / FR-12.4.
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] AdminLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { error = "validation_error", message = "Email and password are required" });
        }

        var result = await _authService.LoginAsync(request.Email, request.Password);

        if (!result.IsSuccess)
        {
            return Unauthorized(new { error = "unauthorized", message = result.ErrorMessage ?? "Invalid credentials" });
        }

        AdminAuthTokenHelper.SetCookie(HttpContext, result.Token!);

        return Ok(new
        {
            token = result.Token,
            role = result.Admin!.Role,
            email = result.Admin.Email
        });
    }

    /// <summary>
    /// GET /admin/auth/me — Current authenticated admin.
    /// </summary>
    [HttpGet("me")]
    public IActionResult Me()
    {
        var admin = HttpContext.Items["AdminUser"] as DayTrace.Domain.Entities.AdminUser;
        if (admin == null)
        {
            return Unauthorized(new { error = "unauthorized", message = "Not authenticated" });
        }

        return Ok(new
        {
            id = admin.Id,
            role = admin.Role,
            email = admin.Email
        });
    }

    /// <summary>
    /// POST /admin/auth/logout — Log out admin session.
    /// </summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var adminContext = HttpContext.Items["AdminUser"] as DayTrace.Domain.Entities.AdminUser;
        var token = AdminAuthTokenHelper.ExtractToken(HttpContext.Request);

        if (adminContext == null || string.IsNullOrEmpty(token))
        {
            AdminAuthTokenHelper.DeleteCookie(HttpContext);
            return Unauthorized(new { error = "unauthorized", message = "Not authenticated" });
        }

        await _authService.LogoutAsync(token, adminContext.Id);
        AdminAuthTokenHelper.DeleteCookie(HttpContext);
        return Ok(new { message = "Logged out" });
    }
}

public class AdminLoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
