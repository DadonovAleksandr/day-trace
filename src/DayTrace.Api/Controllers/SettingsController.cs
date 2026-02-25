using DayTrace.Api.Middleware;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace DayTrace.Api.Controllers;

[ApiController]
[Route("settings")]
public class SettingsController : ControllerBase
{
    private readonly IUserSettingsRepository _settingsRepo;
    private readonly ITimezoneHistoryRepository _tzHistoryRepo;
    private readonly IWeekScheduleHistoryRepository _weekScheduleRepo;
    private readonly IEventRepository _eventRepo;
    private readonly ISummaryRepository _summaryRepo;
    private readonly DateCalculationService _dateService;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(
        IUserSettingsRepository settingsRepo,
        ITimezoneHistoryRepository tzHistoryRepo,
        IWeekScheduleHistoryRepository weekScheduleRepo,
        IEventRepository eventRepo,
        ISummaryRepository summaryRepo,
        DateCalculationService dateService,
        ILogger<SettingsController> logger)
    {
        _settingsRepo = settingsRepo;
        _tzHistoryRepo = tzHistoryRepo;
        _weekScheduleRepo = weekScheduleRepo;
        _eventRepo = eventRepo;
        _summaryRepo = summaryRepo;
        _dateService = dateService;
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
            WeekEnd = settings.WeekEnd,
            ShowWisdom = settings.ShowWisdom,
            WisdomDuration = settings.WisdomDuration,
            ImportanceEnabled = settings.ImportanceEnabled,
            SatisfactionEnabled = settings.SatisfactionEnabled
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

        // Handle week_end change (US-021) — after timezone so today_local is calculated in new TZ
        if (!string.IsNullOrEmpty(request.WeekEnd) && request.WeekEnd != settings.WeekEnd)
        {
            var weekEndResult = await HandleWeekEndChangeAsync(userId, settings, request.WeekEnd, ct);
            if (weekEndResult != null)
                return weekEndResult;
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

        // Update show_wisdom if provided
        if (request.ShowWisdom.HasValue)
        {
            settings.ShowWisdom = request.ShowWisdom.Value;
        }

        // Update wisdom_duration if provided (clamp to 3..60 seconds)
        if (request.WisdomDuration.HasValue)
        {
            settings.WisdomDuration = Math.Clamp(request.WisdomDuration.Value, 3, 60);
        }

        // Update importance_enabled if provided
        if (request.ImportanceEnabled.HasValue)
        {
            settings.ImportanceEnabled = request.ImportanceEnabled.Value;
        }

        // Update satisfaction_enabled if provided
        if (request.SatisfactionEnabled.HasValue)
        {
            settings.SatisfactionEnabled = request.SatisfactionEnabled.Value;
        }

        await _settingsRepo.UpdateAsync(settings, ct);

        _logger.LogInformation("Settings updated for user_id={UserId}", userId);

        return Ok(new SettingsResponse
        {
            Timezone = settings.Timezone,
            ReminderTime = settings.ReminderTime.ToString("HH:mm"),
            ReminderEnabled = settings.ReminderEnabled,
            WeekEnd = settings.WeekEnd,
            ShowWisdom = settings.ShowWisdom,
            WisdomDuration = settings.WisdomDuration,
            ImportanceEnabled = settings.ImportanceEnabled,
            SatisfactionEnabled = settings.SatisfactionEnabled
        });
    }

    /// <summary>
    /// Handles timezone change with IANA validation (US-020).
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

    /// <summary>
    /// Handles week_end change with transition period computation (US-021, FR-4.3).
    /// Returns an error IActionResult if validation fails, or null if OK (settings updated in-place).
    /// </summary>
    private async Task<IActionResult?> HandleWeekEndChangeAsync(
        long userId, UserSettings settings, string newWeekEnd, CancellationToken ct)
    {
        // Validate day name
        DayOfWeek newDay;
        try
        {
            newDay = DateCalculationService.ParseDayOfWeek(newWeekEnd);
        }
        catch (ArgumentException)
        {
            return BadRequest(new { error = "validation_error", message = $"Invalid week_end day: {newWeekEnd}" });
        }

        // Same week_end as current → no-op, no transition
        if (string.Equals(newWeekEnd, settings.WeekEnd, StringComparison.OrdinalIgnoreCase))
            return null;

        var oldDay = DateCalculationService.ParseDayOfWeek(settings.WeekEnd);
        var todayLocal = _dateService.GetTodayLocal(settings.Timezone);

        // Check for unresolved previous transition (409 transition_pending)
        var latestSchedule = await _weekScheduleRepo.GetLatestAsync(userId, ct);
        if (latestSchedule?.TransitionStart != null && latestSchedule?.TransitionEnd != null)
        {
            var resolved = await IsTransitionResolvedAsync(
                userId, latestSchedule.TransitionStart.Value, latestSchedule.TransitionEnd.Value, ct);

            if (!resolved)
            {
                return Conflict(new
                {
                    error = "transition_pending",
                    message = "Previous week_end transition has not been resolved yet",
                    transition_start = latestSchedule.TransitionStart.Value.ToString("yyyy-MM-dd"),
                    transition_end = latestSchedule.TransitionEnd.Value.ToString("yyyy-MM-dd"),
                    hint = "Wait for the transition period summary to be generated, or it auto-resolves when 0 events exist in the transition period"
                });
            }
        }

        // Compute transition period per FR-4.3
        var (transitionStart, transitionEnd, firstNewWeekStart) =
            ComputeTransition(oldDay, newDay, todayLocal);

        var oldWeekEnd = settings.WeekEnd;

        // Create new week_schedule_history record with effective_from = first new week start
        var newSchedule = new WeekScheduleHistory
        {
            UserId = userId,
            WeekEnd = newWeekEnd,
            EffectiveFromLocalDate = firstNewWeekStart,
            TransitionStart = transitionStart,
            TransitionEnd = transitionEnd,
            CreatedAt = DateTime.UtcNow
        };
        await _weekScheduleRepo.CreateAsync(newSchedule, ct);

        // Update settings in-place (will be saved by caller)
        settings.WeekEnd = newWeekEnd;

        _logger.LogInformation(
            "Week_end changed for user_id={UserId} from {OldDay} to {NewDay}, transition=[{Start}..{End}], first_new_week_start={FirstNewWeekStart}",
            userId, oldWeekEnd, newWeekEnd,
            transitionStart.ToString("yyyy-MM-dd"),
            transitionEnd.ToString("yyyy-MM-dd"),
            firstNewWeekStart.ToString("yyyy-MM-dd"));

        return null; // Success — settings updated in-place
    }

    /// <summary>
    /// Computes transition period between old and new week_end schedules (FR-4.3).
    /// Returns (transition_start, transition_end, first_new_week_start).
    /// Transition period length: 1-6 days.
    /// </summary>
    internal static (DateOnly TransitionStart, DateOnly TransitionEnd, DateOnly FirstNewWeekStart) ComputeTransition(
        DayOfWeek oldWeekEnd, DayOfWeek newWeekEnd, DateOnly changeDate)
    {
        // Step 1: Find end of current week (next old_week_end on or after changeDate)
        var currentWeekEndDate = changeDate;
        while (currentWeekEndDate.DayOfWeek != oldWeekEnd)
            currentWeekEndDate = currentWeekEndDate.AddDays(1);

        // Step 2: Transition starts the day after current week ends
        var transitionStart = currentWeekEndDate.AddDays(1);

        // Step 3: Find first new week start (day after new_week_end)
        var newWeekStartDay = (DayOfWeek)(((int)newWeekEnd + 1) % 7);

        var firstNewWeekStart = transitionStart;
        while (firstNewWeekStart.DayOfWeek != newWeekStartDay)
            firstNewWeekStart = firstNewWeekStart.AddDays(1);

        // Step 4: Transition ends the day before the first new week starts
        var transitionEnd = firstNewWeekStart.AddDays(-1);

        return (transitionStart, transitionEnd, firstNewWeekStart);
    }

    /// <summary>
    /// Checks whether a previous transition period has been resolved.
    /// Resolved = summary.status == 'generated' OR count of non-deleted events == 0.
    /// </summary>
    private async Task<bool> IsTransitionResolvedAsync(
        long userId, DateOnly transitionStart, DateOnly transitionEnd, CancellationToken ct)
    {
        // Check 1: 0 non-deleted events → auto-resolved
        var eventCount = await _eventRepo.CountByPeriodAsync(userId, transitionStart, transitionEnd, ct);
        if (eventCount == 0)
            return true;

        // Check 2: summary with status=generated exists for the transition period
        var summary = await _summaryRepo.GetAsync(userId, "transition", transitionStart, transitionEnd, ct);
        if (summary != null && summary.Status == "generated")
            return true;

        return false;
    }

}

public class SettingsResponse
{
    public string Timezone { get; set; } = string.Empty;
    public string ReminderTime { get; set; } = string.Empty;
    public bool ReminderEnabled { get; set; }
    public string WeekEnd { get; set; } = string.Empty;
    public bool ShowWisdom { get; set; }
    public int WisdomDuration { get; set; }
    public bool ImportanceEnabled { get; set; }
    public bool SatisfactionEnabled { get; set; }
}

public class UpdateSettingsRequest
{
    public string? ReminderTime { get; set; }
    public bool? ReminderEnabled { get; set; }
    public string? Timezone { get; set; }
    public string? WeekEnd { get; set; }
    public bool? ShowWisdom { get; set; }
    public int? WisdomDuration { get; set; }
    public bool? ImportanceEnabled { get; set; }
    public bool? SatisfactionEnabled { get; set; }
}
