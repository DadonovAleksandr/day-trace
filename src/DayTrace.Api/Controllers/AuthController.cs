using DayTrace.Domain.Services;
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

    public AuthController(
        TelegramAuthService authService,
        IOptions<TelegramBotOptions> botOptions,
        ILogger<AuthController> logger)
    {
        _authService = authService;
        _botOptions = botOptions.Value;
        _logger = logger;
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

            // Replay case: User may be null (cached token returned without re-loading user)
            return Ok(new TelegramAuthResponse
            {
                Token = result.Token,
                UserId = result.User?.Id ?? 0,
                IsNew = result.IsNew,
                Timezone = result.User?.Settings?.Timezone ?? "UTC"
            });
        }
        catch (AuthenticationException ex)
        {
            _logger.LogWarning("Telegram auth failed: {ErrorCode} - {Message}", ex.ErrorCode, ex.Message);
            return Unauthorized(new { error = ex.ErrorCode, message = ex.Message });
        }
    }
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
    public string Timezone { get; set; } = "UTC";
}
