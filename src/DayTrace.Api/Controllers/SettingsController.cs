using DayTrace.Api.Middleware;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DayTrace.Api.Controllers;

[ApiController]
[Route("settings")]
public class SettingsController : ControllerBase
{
    private readonly IUserSettingsRepository _settingsRepo;
    private readonly ITimezoneHistoryRepository _tzHistoryRepo;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(
        IUserSettingsRepository settingsRepo,
        ITimezoneHistoryRepository tzHistoryRepo,
        ILogger<SettingsController> logger)
    {
        _settingsRepo = settingsRepo;
        _tzHistoryRepo = tzHistoryRepo;
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
    /// PUT /settings — update settings (US-019 basic + US-020 timezone).
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

        // Handle timezone change first (per FR-13.1: timezone applied first when both present)
        if (!string.IsNullOrEmpty(request.Timezone) && request.Timezone != settings.Timezone)
        {
            var tzResult = await HandleTimezoneChangeAsync(userId, settings, request.Timezone, ct);
            if (tzResult != null)
                return tzResult;
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

    /// <summary>
    /// Handles timezone change with IANA validation and 24-hour cooldown (US-020).
    /// Returns an error IActionResult if validation fails, or null if OK (settings updated in-place).
    /// </summary>
    private async Task<IActionResult?> HandleTimezoneChangeAsync(
        long userId, UserSettings settings, string newTimezone, CancellationToken ct)
    {
        // Validate IANA timezone string
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(newTimezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return BadRequest(new { error = "validation_error", message = $"Invalid IANA timezone: {newTimezone}" });
        }

        // Check 24-hour cooldown
        var latestTzChange = await _tzHistoryRepo.GetLatestAsync(userId, ct);
        if (latestTzChange != null)
        {
            var elapsed = DateTime.UtcNow - latestTzChange.CreatedAt;
            if (elapsed.TotalHours < 24)
            {
                var retryAfterSeconds = (int)(TimeSpan.FromHours(24) - elapsed).TotalSeconds;
                return StatusCode(429, new
                {
                    error = "timezone_change_cooldown",
                    message = "Timezone can only be changed once every 24 hours",
                    retry_after_seconds = retryAfterSeconds
                });
            }
        }

        // Apply timezone change
        settings.Timezone = newTimezone;

        // Create timezone_history record (FR-2.2)
        var tzHistory = new TimezoneHistory
        {
            UserId = userId,
            Timezone = newTimezone,
            EffectiveFrom = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
        await _tzHistoryRepo.CreateAsync(tzHistory, ct);

        _logger.LogInformation(
            "Timezone changed for user_id={UserId} to {Timezone}",
            userId, newTimezone);

        return null; // Success — settings updated in-place
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
    public string? Timezone { get; set; }
    public string? WeekEnd { get; set; }
}
