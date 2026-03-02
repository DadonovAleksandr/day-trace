using DayTrace.Api.Middleware;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace DayTrace.Api.Controllers;

[ApiController]
[Route("events")]
public class EventsController : ControllerBase
{
    private readonly IEventRepository _eventRepo;
    private readonly DateCalculationService _dateService;
    private readonly EventLockService _lockService;
    private readonly ISubscriptionRepository _subscriptionRepo;
    private readonly SubscriptionService _subscriptionService;
    private readonly ILogger<EventsController> _logger;

    public EventsController(
        IEventRepository eventRepo,
        DateCalculationService dateService,
        EventLockService lockService,
        ISubscriptionRepository subscriptionRepo,
        SubscriptionService subscriptionService,
        ILogger<EventsController> logger)
    {
        _eventRepo = eventRepo;
        _dateService = dateService;
        _lockService = lockService;
        _subscriptionRepo = subscriptionRepo;
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    /// <summary>
    /// POST /events — create event with validation (FR-1, FR-13.1).
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateEvent(
        [FromBody] CreateEventRequest request,
        CancellationToken ct)
    {
        var userId = HttpContext.GetUserId();
        var timezone = HttpContext.GetTimezone();

        // Validate text: 1..500 chars
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length < 1 || request.Text.Length > 500)
        {
            return BadRequest(new { error = "validation_error", message = "Text must be between 1 and 500 characters" });
        }

        // Validate importance: 1..5
        if (request.Importance < 1 || request.Importance > 5)
        {
            return BadRequest(new { error = "validation_error", message = "Importance must be between 1 and 5" });
        }

        // Determine local_date
        DateOnly localDate;
        if (!string.IsNullOrEmpty(request.LocalDate))
        {
            if (!DateOnly.TryParseExact(request.LocalDate, "yyyy-MM-dd", out localDate))
            {
                return BadRequest(new { error = "validation_error", message = "local_date must be in YYYY-MM-DD format" });
            }
        }
        else
        {
            localDate = _dateService.GetTodayLocal(timezone);
        }

        // Validate date range: [today-30, today]
        var (minDate, maxDate) = _dateService.GetBackdateWindow(timezone);
        if (localDate < minDate || localDate > maxDate)
        {
            return BadRequest(new { error = "date_out_of_range", message = $"local_date must be between {minDate:yyyy-MM-dd} and {maxDate:yyyy-MM-dd}" });
        }

        // One event per day: check if event already exists for this date
        var existingEvent = await _eventRepo.GetByUserAndDateAsync(userId, localDate, ct);
        if (existingEvent != null)
        {
            return Conflict(new { error = "event_exists", message = "Событие на этот день уже создано.", existing_event_id = existingEvent.Id });
        }

        var evt = new Event
        {
            UserId = userId,
            Text = request.Text,
            Importance = request.Importance,
            LocalDate = localDate,
            CreatedAt = DateTime.UtcNow
        };

        evt = await _eventRepo.CreateAsync(evt, ct);

        _logger.LogInformation("Event created: event_id={EventId}, user_id={UserId}, local_date={LocalDate}",
            evt.Id, userId, localDate);

        // Start trial on first event creation
        var sub = await _subscriptionRepo.GetByUserIdAsync(userId);
        if (sub?.TrialStartedAt == null)
        {
            await _subscriptionService.StartTrialAsync(userId, evt.LocalDate);
        }

        return StatusCode(201, new CreateEventResponse
        {
            Id = evt.Id,
            Text = evt.Text,
            Importance = evt.Importance,
            LocalDate = evt.LocalDate.ToString("yyyy-MM-dd"),
            CreatedAt = evt.CreatedAt
        });
    }

    /// <summary>
    /// GET /events — list events with filtering & pagination (FR-13).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> ListEvents(
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int limit = 20,
        [FromQuery] string? cursor = null,
        CancellationToken ct = default)
    {
        var userId = HttpContext.GetUserId();
        var timezone = HttpContext.GetTimezone();

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

        // Default: current day in user TZ
        if (!fromDate.HasValue && !toDate.HasValue)
        {
            var today = _dateService.GetTodayLocal(timezone);
            fromDate = today;
            toDate = today;
        }

        var (items, nextCursor) = await _eventRepo.ListAsync(userId, fromDate, toDate, limit, cursor, ct);

        return Ok(new
        {
            items = items.Select(e => new
            {
                id = e.Id,
                text = e.Text,
                importance = e.Importance,
                local_date = e.LocalDate.ToString("yyyy-MM-dd"),
                created_at = e.CreatedAt,
                updated_at = e.UpdatedAt
            }),
            next_cursor = nextCursor
        });
    }

    /// <summary>
    /// PATCH /events/{id} — edit event (FR-1).
    /// </summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> EditEvent(Guid id, [FromBody] EditEventRequest request, CancellationToken ct)
    {
        var userId = HttpContext.GetUserId();

        var evt = await _eventRepo.GetByIdAsync(id, userId, ct);
        if (evt == null)
            return NotFound(new { error = "not_found", message = "Event not found" });

        // Check if event is locked by a generated weekly summary
        var (locked, lockedBy) = await _lockService.IsEventLockedAsync(userId, evt.LocalDate, ct);
        if (locked)
            return UnprocessableEntity(new { error = "locked_by_summary", message = "Итог недели уже сформирован. Изменение невозможно.", locked_by = lockedBy });

        // Validate and apply text update
        if (request.Text != null)
        {
            if (request.Text.Length < 1 || request.Text.Length > 500)
                return BadRequest(new { error = "validation_error", message = "Text must be between 1 and 500 characters" });
            evt.Text = request.Text;
        }

        // Validate and apply importance update
        if (request.Importance.HasValue)
        {
            if (request.Importance.Value < 1 || request.Importance.Value > 5)
                return BadRequest(new { error = "validation_error", message = "Importance must be between 1 and 5" });
            evt.Importance = request.Importance.Value;
        }

        evt.UpdatedAt = DateTime.UtcNow;
        await _eventRepo.UpdateAsync(evt, ct);

        return Ok(new
        {
            id = evt.Id,
            text = evt.Text,
            importance = evt.Importance,
            local_date = evt.LocalDate.ToString("yyyy-MM-dd"),
            created_at = evt.CreatedAt,
            updated_at = evt.UpdatedAt
        });
    }

    /// <summary>
    /// DELETE /events/{id} — soft delete event (FR-1).
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> DeleteEvent(Guid id, CancellationToken ct)
    {
        var userId = HttpContext.GetUserId();

        var evt = await _eventRepo.GetByIdAsync(id, userId, ct);
        if (evt == null)
            return NotFound(new { error = "not_found", message = "Event not found" });

        // Check if event is locked by a generated weekly summary
        var (locked, lockedBy) = await _lockService.IsEventLockedAsync(userId, evt.LocalDate, ct);
        if (locked)
            return UnprocessableEntity(new { error = "locked_by_summary", message = "Итог недели уже сформирован. Удаление невозможно.", locked_by = lockedBy });

        evt.DeletedAt = DateTime.UtcNow;
        await _eventRepo.UpdateAsync(evt, ct);

        return NoContent();
    }
}

public class CreateEventRequest
{
    public string Text { get; set; } = string.Empty;
    public int Importance { get; set; }
    public string? LocalDate { get; set; }
}

public class CreateEventResponse
{
    public Guid Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public int Importance { get; set; }
    public string LocalDate { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class EditEventRequest
{
    public string? Text { get; set; }
    public int? Importance { get; set; }
}
