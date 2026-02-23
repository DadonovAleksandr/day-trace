using DayTrace.Api.Middleware;
using DayTrace.Domain.Interfaces;
using DayTrace.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace DayTrace.Api.Controllers;

[ApiController]
[Route("summaries")]
public class SummariesController : ControllerBase
{
    private readonly HighlightService _highlightService;
    private readonly ISummaryRepository _summaryRepo;
    private readonly EventLockService _lockService;
    private readonly ILogger<SummariesController> _logger;

    private static readonly HashSet<string> ValidPeriodTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "weekly", "monthly", "yearly"
    };

    public SummariesController(
        HighlightService highlightService,
        ISummaryRepository summaryRepo,
        EventLockService lockService,
        ILogger<SummariesController> logger)
    {
        _highlightService = highlightService;
        _summaryRepo = summaryRepo;
        _lockService = lockService;
        _logger = logger;
    }

    /// <summary>
    /// PUT /summaries/{periodType}/highlight — set the highlight event for a period.
    /// X-Client-Operation-Id required for dedupe (handled by middleware).
    /// </summary>
    [HttpPut("{periodType}/highlight")]
    public async Task<IActionResult> SetHighlight(
        string periodType,
        [FromBody] SetHighlightRequest request,
        CancellationToken ct)
    {
        var userId = HttpContext.GetUserId();

        if (request.EventId == Guid.Empty)
            return BadRequest(new { error = "validation_error", message = "event_id is required" });

        if (string.IsNullOrEmpty(request.PeriodStart) || string.IsNullOrEmpty(request.PeriodEnd))
            return BadRequest(new { error = "validation_error", message = "period_start and period_end are required" });

        if (!DateOnly.TryParseExact(request.PeriodStart, "yyyy-MM-dd", out var periodStart))
            return BadRequest(new { error = "invalid_period", message = "period_start must be in YYYY-MM-DD format" });

        if (!DateOnly.TryParseExact(request.PeriodEnd, "yyyy-MM-dd", out var periodEnd))
            return BadRequest(new { error = "invalid_period", message = "period_end must be in YYYY-MM-DD format" });

        var result = await _highlightService.SetHighlightAsync(
            userId, request.EventId, periodType, periodStart, periodEnd, ct);

        if (!result.Success)
        {
            return result.Error switch
            {
                "event_not_found" => NotFound(new { error = result.Error, message = result.Message }),
                "locked_by_summary" => UnprocessableEntity(new { error = result.Error, message = result.Message, locked_by = result.LockedBy }),
                _ => BadRequest(new { error = result.Error, message = result.Message })
            };
        }

        var s = result.Summary!;
        _logger.LogInformation(
            "Highlight set: user_id={UserId}, period_type={PeriodType}, period=[{Start}..{End}], highlight_event_id={EventId}",
            userId, periodType, periodStart.ToString("yyyy-MM-dd"), periodEnd.ToString("yyyy-MM-dd"), request.EventId);

        return Ok(new
        {
            id = s.Id,
            period_type = s.PeriodType,
            period_start = s.PeriodStart.ToString("yyyy-MM-dd"),
            period_end = s.PeriodEnd.ToString("yyyy-MM-dd"),
            status = s.Status,
            version = s.Version,
            highlight_event_id = s.HighlightEventId,
            last_generated_at = s.LastGeneratedAt
        });
    }

    /// <summary>
    /// GET /summaries/{periodType} — list summaries with pagination.
    /// </summary>
    [HttpGet("{periodType}")]
    public async Task<IActionResult> ListSummaries(
        string periodType,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int limit = 20,
        [FromQuery] string? cursor = null,
        CancellationToken ct = default)
    {
        var userId = HttpContext.GetUserId();

        var normalizedPeriodType = periodType.ToLowerInvariant();
        if (!ValidPeriodTypes.Contains(normalizedPeriodType))
            return BadRequest(new { error = "invalid_period", message = $"periodType must be one of: weekly, monthly, yearly. Got: {periodType}" });

        if (limit < 1 || limit > 100)
            limit = 20;

        DateOnly? fromDate = null;
        DateOnly? toDate = null;

        if (!string.IsNullOrEmpty(from))
        {
            if (DateOnly.TryParseExact(from, "yyyy-MM-dd", out var fd))
                fromDate = fd;
        }

        if (!string.IsNullOrEmpty(to))
        {
            if (DateOnly.TryParseExact(to, "yyyy-MM-dd", out var td))
                toDate = td;
        }

        var (items, nextCursor) = await _summaryRepo.ListAsync(
            userId, normalizedPeriodType, fromDate, toDate, limit, cursor, ct);

        return Ok(new
        {
            items = items.Select(s => new
            {
                id = s.Id,
                period_type = s.PeriodType,
                period_start = s.PeriodStart.ToString("yyyy-MM-dd"),
                period_end = s.PeriodEnd.ToString("yyyy-MM-dd"),
                status = s.Status,
                version = s.Version,
                content = s.Content,
                highlight_event_id = s.HighlightEventId,
                last_generated_at = s.LastGeneratedAt
            }),
            next_cursor = nextCursor
        });
    }
}

public class SetHighlightRequest
{
    public Guid EventId { get; set; }
    public string PeriodStart { get; set; } = string.Empty;
    public string PeriodEnd { get; set; } = string.Empty;
}
