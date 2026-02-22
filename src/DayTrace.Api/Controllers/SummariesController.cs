using DayTrace.Api.Middleware;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace DayTrace.Api.Controllers;

[ApiController]
[Route("summaries")]
public class SummariesController : ControllerBase
{
    private readonly PeriodJobCreationService _periodJobService;
    private readonly PeriodSelectionService _periodSelectionService;
    private readonly ISummaryRepository _summaryRepo;
    private readonly IUserSettingsRepository _settingsRepo;
    private readonly IPromptDeliveryRepository _promptDeliveryRepo;
    private readonly EventLockService _lockService;
    private readonly ILogger<SummariesController> _logger;

    private static readonly HashSet<string> ValidPeriodTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "weekly", "monthly", "yearly"
    };

    public SummariesController(
        PeriodJobCreationService periodJobService,
        PeriodSelectionService periodSelectionService,
        ISummaryRepository summaryRepo,
        IUserSettingsRepository settingsRepo,
        IPromptDeliveryRepository promptDeliveryRepo,
        EventLockService lockService,
        ILogger<SummariesController> logger)
    {
        _periodJobService = periodJobService;
        _periodSelectionService = periodSelectionService;
        _summaryRepo = summaryRepo;
        _settingsRepo = settingsRepo;
        _promptDeliveryRepo = promptDeliveryRepo;
        _lockService = lockService;
        _logger = logger;
    }

    /// <summary>
    /// POST /summaries/{periodType}/run — manual trigger for summary generation (US-031).
    /// Force re-run mode: increments run_number.
    /// X-Client-Operation-Id required for dedupe (handled by middleware).
    /// </summary>
    [HttpPost("{periodType}/run")]
    public async Task<IActionResult> ManualRun(
        string periodType,
        [FromBody] ManualRunRequest? request,
        CancellationToken ct)
    {
        var userId = HttpContext.GetUserId();
        var timezone = HttpContext.GetTimezone();

        // Validate periodType
        var normalizedPeriodType = periodType.ToLowerInvariant();
        if (!ValidPeriodTypes.Contains(normalizedPeriodType))
        {
            return BadRequest(new { error = "invalid_period", message = $"periodType must be one of: weekly, monthly, yearly. Got: {periodType}" });
        }

        // Determine period boundaries
        DateOnly periodStart, periodEnd;

        if (request?.PeriodStart != null && request?.PeriodEnd != null)
        {
            // Explicit dates provided
            if (!DateOnly.TryParseExact(request.PeriodStart, "yyyy-MM-dd", out periodStart))
            {
                return BadRequest(new { error = "invalid_period", message = "period_start must be in YYYY-MM-DD format" });
            }

            if (!DateOnly.TryParseExact(request.PeriodEnd, "yyyy-MM-dd", out periodEnd))
            {
                return BadRequest(new { error = "invalid_period", message = "period_end must be in YYYY-MM-DD format" });
            }

            if (periodStart > periodEnd)
            {
                return BadRequest(new { error = "invalid_period", message = "period_start must be <= period_end" });
            }
        }
        else if (request?.PeriodStart != null || request?.PeriodEnd != null)
        {
            // Only one of start/end provided
            return BadRequest(new { error = "invalid_period", message = "Both period_start and period_end must be provided, or neither" });
        }
        else
        {
            // No explicit dates → deterministic period selection (US-032)
            var selection = await _periodSelectionService.SelectPeriodAsync(
                userId, normalizedPeriodType, timezone, ct);
            periodStart = selection.PeriodStart;
            periodEnd = selection.PeriodEnd;
        }

        // Check if summary regeneration is locked by a higher-level summary
        var (locked, lockedBy) = await _lockService.IsSummaryLockedAsync(userId, normalizedPeriodType, periodStart, periodEnd, ct);
        if (locked)
        {
            var lockMessage = lockedBy switch
            {
                "monthly" => "Итог месяца уже сформирован. Переформирование недели невозможно.",
                "yearly" => "Итог года уже сформирован. Переформирование месяца невозможно.",
                _ => "Переформирование невозможно."
            };
            return UnprocessableEntity(new { error = "locked_by_summary", message = lockMessage, locked_by = lockedBy });
        }

        // Create period job via force_rerun mode (FR-8.2b)
        var result = await _periodJobService.CreateAsync(
            userId, normalizedPeriodType, periodStart, periodEnd,
            PeriodJobCreationService.CreateMode.ForceRerun, ct);

        if (!result.Success)
        {
            if (result.Reason == "empty_period")
            {
                return BadRequest(new
                {
                    error = "empty_period",
                    message = $"No events or existing summary found for period [{periodStart:yyyy-MM-dd}..{periodEnd:yyyy-MM-dd}]"
                });
            }

            return StatusCode(500, new { error = "internal_error", message = "Failed to create period job" });
        }

        _logger.LogInformation(
            "Manual run triggered: user_id={UserId}, period_type={PeriodType}, period=[{Start}..{End}], job_id={JobId}, reason={Reason}",
            userId, normalizedPeriodType,
            periodStart.ToString("yyyy-MM-dd"),
            periodEnd.ToString("yyyy-MM-dd"),
            result.Job?.Id, result.Reason);

        // Record prompt_delivery with channel=manual (US-041)
        try
        {
            var promptId = $"manual_{normalizedPeriodType}_{userId}_{periodStart:yyyy-MM-dd}_{periodEnd:yyyy-MM-dd}_{DateTime.UtcNow:yyyyMMddHHmmss}";
            var delivery = new PromptDelivery
            {
                PromptId = promptId,
                UserId = userId,
                PeriodType = normalizedPeriodType,
                PeriodStart = periodStart,
                PeriodEnd = periodEnd,
                SentAt = DateTime.UtcNow,
                Channel = "manual",
                Status = "sent"
            };
            await _promptDeliveryRepo.CreateAsync(delivery, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to record prompt_delivery for manual run, continuing");
        }

        return Ok(new ManualRunResponse
        {
            JobId = result.Job!.Id,
            PeriodType = normalizedPeriodType,
            PeriodStart = periodStart.ToString("yyyy-MM-dd"),
            PeriodEnd = periodEnd.ToString("yyyy-MM-dd"),
            RunNumber = result.Job.RunNumber,
            Status = result.Job.Status,
            SummaryId = result.Summary?.Id
        });
    }

    /// <summary>
    /// GET /summaries/{periodType} — list summaries with pagination (US-033).
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

        // Validate periodType
        var normalizedPeriodType = periodType.ToLowerInvariant();
        if (!ValidPeriodTypes.Contains(normalizedPeriodType))
        {
            return BadRequest(new { error = "invalid_period", message = $"periodType must be one of: weekly, monthly, yearly. Got: {periodType}" });
        }

        // Validate limit
        if (limit < 1 || limit > 100)
            limit = 20;

        // Parse date filters
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
                last_generated_at = s.LastGeneratedAt
            }),
            next_cursor = nextCursor
        });
    }
}

public class ManualRunRequest
{
    public string? PeriodStart { get; set; }
    public string? PeriodEnd { get; set; }
}

public class ManualRunResponse
{
    public long JobId { get; set; }
    public string PeriodType { get; set; } = string.Empty;
    public string PeriodStart { get; set; } = string.Empty;
    public string PeriodEnd { get; set; } = string.Empty;
    public int RunNumber { get; set; }
    public string Status { get; set; } = string.Empty;
    public long? SummaryId { get; set; }
}
