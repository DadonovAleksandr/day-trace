using DayTrace.Api.Middleware;
using DayTrace.Domain.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DayTrace.Api.Controllers;

/// <summary>
/// GET /admin/metrics/dashboard — Aggregated dashboard metrics.
/// Per US-054 / FR-11, docs/METRICS.md.
/// Requires analyst role minimum.
/// </summary>
[ApiController]
[Route("admin/metrics")]
public class AdminMetricsController : ControllerBase
{
    private readonly IMetricsRepository _metricsRepo;
    private readonly ILogger<AdminMetricsController> _logger;

    public AdminMetricsController(IMetricsRepository metricsRepo, ILogger<AdminMetricsController> logger)
    {
        _metricsRepo = metricsRepo;
        _logger = logger;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard()
    {
        var admin = HttpContext.GetAdminUser();
        if (admin == null)
            return Unauthorized(new { error = "unauthorized", message = "Not authenticated" });

        var now = DateTime.UtcNow;

        var dau = await _metricsRepo.GetDailyActiveUsersAsync(now.Date);
        var wau = await _metricsRepo.GetWeeklyActiveUsersAsync(now);
        var mau = await _metricsRepo.GetMonthlyActiveUsersAsync(now);
        var reminderConversion = await _metricsRepo.GetReminderConversionAsync(now);
        var promptConversion = await _metricsRepo.GetPromptConversionAsync(now);

        return Ok(new
        {
            dau,
            wau,
            mau,
            reminder_conversion = new
            {
                converted = reminderConversion.converted,
                total = reminderConversion.total,
                rate = reminderConversion.total > 0
                    ? Math.Round((double)reminderConversion.converted / reminderConversion.total, 4)
                    : 0.0
            },
            prompt_conversion = new
            {
                converted = promptConversion.converted,
                total = promptConversion.total,
                rate = promptConversion.total > 0
                    ? Math.Round((double)promptConversion.converted / promptConversion.total, 4)
                    : 0.0
            },
            calculated_at = now
        });
    }
}
