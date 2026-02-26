using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Domain.Services;
using DayTrace.Domain.Utilities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using DayTrace.Bot.Configuration;

namespace DayTrace.Api.Controllers;

[ApiController]
[Route("auth")]
public class AuthController : ControllerBase
{
    private readonly TelegramAuthService _authService;
    private readonly TelegramBotOptions _botOptions;
    private readonly ILogger<AuthController> _logger;
    private readonly IWebHostEnvironment _env;
    private readonly UserRegistrationService _registrationService;
    private readonly ISessionRepository _sessionRepo;

    public AuthController(
        TelegramAuthService authService,
        IOptions<TelegramBotOptions> botOptions,
        ILogger<AuthController> logger,
        IWebHostEnvironment env,
        UserRegistrationService registrationService,
        ISessionRepository sessionRepo)
    {
        _authService = authService;
        _botOptions = botOptions.Value;
        _logger = logger;
        _env = env;
        _registrationService = registrationService;
        _sessionRepo = sessionRepo;
    }

    /// <summary>
    /// Authenticate via Telegram Mini App init data (FR-12.1).
    /// </summary>
    [HttpPost("telegram")]
    public async Task<IActionResult> AuthenticateTelegram(
        [FromBody] TelegramAuthRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.InitData))
        {
            return BadRequest(new { error = "validation_error", message = "initData is required" });
        }

        try
        {
            var result = await _authService.AuthenticateAsync(
                request.InitData,
                _botOptions.BotToken,
                request.Timezone,
                ct);

            if (result.User == null)
            {
                _logger.LogWarning("Telegram auth: authentication succeeded but User is null for token prefix={TokenPrefix}",
                    result.Token.Length > 8 ? result.Token[..8] + "..." : result.Token);
                return Unauthorized(new { error = "auth_incomplete", message = "Authentication succeeded but user data is unavailable" });
            }

            return Ok(new TelegramAuthResponse
            {
                Token = result.Token,
                UserId = result.User.Id,
                IsNew = result.IsNew,
                Timezone = result.User.Settings?.Timezone ?? "Europe/Moscow"
            });
        }
        catch (AuthenticationException ex)
        {
            _logger.LogWarning("Telegram auth failed: {ErrorCode} - {Message}", ex.ErrorCode, ex.Message);
            return Unauthorized(new { error = ex.ErrorCode, message = ex.Message });
        }
    }

    /// <summary>
    /// Dev-only authentication bypass — creates a dev user and returns a session token.
    /// Only available in Development environment.
    /// </summary>
    [HttpPost("dev")]
    public async Task<IActionResult> AuthenticateDev(
        [FromBody] DevAuthRequest? request,
        CancellationToken ct)
    {
        if (!_env.IsDevelopment())
            return NotFound();

        const long devTelegramId = 999_999_999;
        var timezone = request?.Timezone;

        var (user, isNew) = await _registrationService.RegisterAsync(devTelegramId, timezone, ct);

        var token = Guid.NewGuid().ToString("N");
        var tokenHash = CryptoUtils.ComputeSha256(token);

        var session = new UserSession
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            CreatedAt = DateTime.UtcNow
        };
        await _sessionRepo.CreateAsync(session, ct);

        _logger.LogInformation("Dev auth: user_id={UserId}, is_new={IsNew}", user.Id, isNew);

        return Ok(new TelegramAuthResponse
        {
            Token = token,
            UserId = user.Id,
            IsNew = isNew,
            Timezone = user.Settings?.Timezone ?? "Europe/Moscow"
        });
    }
}

public class DevAuthRequest
{
    public string? Timezone { get; set; }
}

public class TelegramAuthRequest
{
    /// <summary>
    /// Raw Telegram init data string (URL-encoded parameters).
    /// </summary>
    public string InitData { get; set; } = string.Empty;

    /// <summary>
    /// Detected timezone from Mini App (Intl.DateTimeFormat().resolvedOptions().timeZone).
    /// </summary>
    public string? Timezone { get; set; }
}

public class TelegramAuthResponse
{
    public string Token { get; set; } = string.Empty;
    public long UserId { get; set; }
    public bool IsNew { get; set; }
    public string Timezone { get; set; } = "Europe/Moscow";
}
