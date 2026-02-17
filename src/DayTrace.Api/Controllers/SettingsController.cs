using DayTrace.Api.Middleware;
using DayTrace.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DayTrace.Api.Controllers;

[ApiController]
[Route("settings")]
public class SettingsController : ControllerBase
{
    private readonly IUserSettingsRepository _settingsRepo;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(
        IUserSettingsRepository settingsRepo,
        ILogger<SettingsController> logger)
    {
        _settingsRepo = settingsRepo;
        _logger = logger;
    }

    /// <summary>
    /// GET /settings — read user settings (US-018).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        var userId = HttpContext.GetUserId();

        var settings = await _settingsRepo.GetByUserIdAsync(userId, ct);
        if (settings == null)
        {
            return NotFound(new { error = "not_found", message = "User settings not found" });
        }

        return Ok(new SettingsResponse
        {
            Timezone = settings.Timezone,
            ReminderTime = settings.ReminderTime.ToString("HH:mm"),
            ReminderEnabled = settings.ReminderEnabled,
            WeekEnd = settings.WeekEnd
        });
    }

    /// <summary>
    /// PUT /settings — update settings (US-019 basic: reminder_time, reminder_enabled).
    /// Timezone and week_end changes are handled in US-020, US-021.
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> UpdateSettings(
        [FromBody] UpdateSettingsRequest request,
        CancellationToken ct)
    {
        var userId = HttpContext.GetUserId();

        var settings = await _settingsRepo.GetByUserIdAsync(userId, ct);
        if (settings == null)
        {
            return NotFound(new { error = "not_found", message = "User settings not found" });
        }

        // Update reminder_time if provided
        if (!string.IsNullOrEmpty(request.ReminderTime))
        {
            if (!TimeOnly.TryParseExact(request.ReminderTime, "HH:mm", out var parsedTime))
            {
                return BadRequest(new { error = "validation_error", message = "reminder_time must be in HH:mm format (24-hour)" });
            }
            settings.ReminderTime = parsedTime;
        }

        // Update reminder_enabled if provided
        if (request.ReminderEnabled.HasValue)
        {
            settings.ReminderEnabled = request.ReminderEnabled.Value;
        }

        await _settingsRepo.UpdateAsync(settings, ct);

        _logger.LogInformation("Settings updated for user_id={UserId}", userId);

        return Ok(new SettingsResponse
        {
            Timezone = settings.Timezone,
            ReminderTime = settings.ReminderTime.ToString("HH:mm"),
            ReminderEnabled = settings.ReminderEnabled,
            WeekEnd = settings.WeekEnd
        });
    }
}

public class SettingsResponse
{
    public string Timezone { get; set; } = string.Empty;
    public string ReminderTime { get; set; } = string.Empty;
    public bool ReminderEnabled { get; set; }
    public string WeekEnd { get; set; } = string.Empty;
}

public class UpdateSettingsRequest
{
    public string? ReminderTime { get; set; }
    public bool? ReminderEnabled { get; set; }
    // Timezone and WeekEnd are handled by separate stories (US-020, US-021)
    // but we accept them here to future-proof the endpoint
    public string? Timezone { get; set; }
    public string? WeekEnd { get; set; }
}
