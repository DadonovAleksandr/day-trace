using DayTrace.Api.Middleware;
using DayTrace.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DayTrace.Api.Controllers;

/// <summary>
/// Admin user management endpoints.
/// Per US-055 / FR-11.
/// </summary>
[ApiController]
[Route("admin/users")]
public class AdminUsersController : ControllerBase
{
    private readonly IUserRepository _userRepo;
    private readonly IUserSettingsRepository _settingsRepo;
    private readonly IEventRepository _eventRepo;
    private readonly ISummaryRepository _summaryRepo;
    private readonly IAdminAuditService _adminAuditService;
    private readonly ILogger<AdminUsersController> _logger;

    public AdminUsersController(
        IUserRepository userRepo,
        IUserSettingsRepository settingsRepo,
        IEventRepository eventRepo,
        ISummaryRepository summaryRepo,
        IAdminAuditService adminAuditService,
        ILogger<AdminUsersController> logger)
    {
        _userRepo = userRepo;
        _settingsRepo = settingsRepo;
        _eventRepo = eventRepo;
        _summaryRepo = summaryRepo;
        _adminAuditService = adminAuditService;
        _logger = logger;
    }

    /// <summary>
    /// GET /admin/users — User list with search/filter, pagination.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListUsers(
        [FromQuery] int limit = 20,
        [FromQuery] int offset = 0,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null)
    {
        var admin = HttpContext.GetAdminUser();
        if (admin == null)
            return Unauthorized(new { error = "unauthorized" });

        var users = await _userRepo.GetAllAsync(limit, offset, search, status);
        var total = await _userRepo.CountAsync(search, status);

        await _adminAuditService.LogSuccessAsync(admin.Id, "list_users", "user", null);

        return Ok(new
        {
            items = users.Select(u => new
            {
                id = u.Id,
                telegram_user_id = u.TelegramUserId,
                status = u.Status,
                created_at = u.CreatedAt,
                timezone = u.Settings?.Timezone,
                reminder_enabled = u.Settings?.ReminderEnabled,
                reminder_time = u.Settings?.ReminderTime,
                week_end = u.Settings?.WeekEnd,
            }),
            total,
            limit,
            offset
        });
    }

    /// <summary>
    /// GET /admin/users/{id} — User detail with settings, event count, summary stats.
    /// </summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetUser(long id)
    {
        var admin = HttpContext.GetAdminUser();
        if (admin == null)
            return Unauthorized(new { error = "unauthorized" });

        var user = await _userRepo.GetByIdWithSettingsAsync(id);
        if (user == null)
            return NotFound(new { error = "not_found", message = "User not found" });

        var eventCount = await _eventRepo.CountByUserAsync(id);
        var summaryCount = await _summaryRepo.CountByUserAsync(id);

        await _adminAuditService.LogSuccessAsync(admin.Id, "view_user", "user", id.ToString());

        return Ok(new
        {
            id = user.Id,
            telegram_user_id = user.TelegramUserId,
            status = user.Status,
            created_at = user.CreatedAt,
            settings = user.Settings != null ? new
            {
                timezone = user.Settings.Timezone,
                reminder_time = user.Settings.ReminderTime,
                reminder_enabled = user.Settings.ReminderEnabled,
                week_end = user.Settings.WeekEnd
            } : null,
            event_count = eventCount,
            summary_count = summaryCount
        });
    }

}
