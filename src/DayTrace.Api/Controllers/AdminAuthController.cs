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

        return Ok(new
        {
            token = result.Token,
            role = result.Admin!.Role,
            email = result.Admin.Email
        });
    }

    /// <summary>
    /// POST /admin/auth/logout — Log out admin session.
    /// </summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        var adminContext = HttpContext.Items["AdminUser"] as DayTrace.Domain.Entities.AdminUser;
        var token = ExtractToken();

        if (adminContext == null || string.IsNullOrEmpty(token))
        {
            return Unauthorized(new { error = "unauthorized", message = "Not authenticated" });
        }

        await _authService.LogoutAsync(token, adminContext.Id);
        return Ok(new { message = "Logged out" });
    }

    private string? ExtractToken()
    {
        var authHeader = HttpContext.Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer "))
            return null;
        return authHeader["Bearer ".Length..].Trim();
    }
}

public class AdminLoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
