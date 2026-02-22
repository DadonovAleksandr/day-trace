using DayTrace.Api.Middleware;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace DayTrace.Api.Controllers;

[ApiController]
[Route("day-rating")]
public class DayRatingController : ControllerBase
{
    private readonly IDayRatingRepository _ratingRepo;
    private readonly DateCalculationService _dateService;
    private readonly ILogger<DayRatingController> _logger;

    public DayRatingController(
        IDayRatingRepository ratingRepo,
        DateCalculationService dateService,
        ILogger<DayRatingController> logger)
    {
        _ratingRepo = ratingRepo;
        _dateService = dateService;
        _logger = logger;
    }

    /// <summary>
    /// GET /day-rating?date=YYYY-MM-DD — get satisfaction rating for a day.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetDayRating(
        [FromQuery] string? date,
        CancellationToken ct)
    {
        var userId = HttpContext.GetUserId();
        var timezone = HttpContext.GetTimezone();

        DateOnly localDate;
        if (!string.IsNullOrEmpty(date))
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out localDate))
                return BadRequest(new { error = "validation_error", message = "date must be in YYYY-MM-DD format" });
        }
        else
        {
            localDate = _dateService.GetTodayLocal(timezone);
        }

        var rating = await _ratingRepo.GetAsync(userId, localDate, ct);

        return Ok(new DayRatingResponse
        {
            LocalDate = localDate.ToString("yyyy-MM-dd"),
            Rating = rating?.Rating,
            UpdatedAt = rating?.UpdatedAt
        });
    }

    /// <summary>
    /// PUT /day-rating — set or update day satisfaction rating (1-5).
    /// </summary>
    [HttpPut]
    public async Task<IActionResult> SetDayRating(
        [FromBody] SetDayRatingRequest request,
        CancellationToken ct)
    {
        var userId = HttpContext.GetUserId();
        var timezone = HttpContext.GetTimezone();

        // Validate rating: 1..5
        if (request.Rating < 1 || request.Rating > 5)
            return BadRequest(new { error = "validation_error", message = "Rating must be between 1 and 5" });

        // Parse date
        DateOnly localDate;
        if (!string.IsNullOrEmpty(request.LocalDate))
        {
            if (!DateOnly.TryParseExact(request.LocalDate, "yyyy-MM-dd", out localDate))
                return BadRequest(new { error = "validation_error", message = "local_date must be in YYYY-MM-DD format" });
        }
        else
        {
            localDate = _dateService.GetTodayLocal(timezone);
        }

        // Validate date range: [today-30, today]
        var (minDate, maxDate) = _dateService.GetBackdateWindow(timezone);
        if (localDate < minDate || localDate > maxDate)
            return BadRequest(new { error = "date_out_of_range", message = $"local_date must be between {minDate:yyyy-MM-dd} and {maxDate:yyyy-MM-dd}" });

        var existing = await _ratingRepo.GetAsync(userId, localDate, ct);

        if (existing != null)
        {
            existing.Rating = request.Rating;
            existing.UpdatedAt = DateTime.UtcNow;
            await _ratingRepo.UpdateAsync(existing, ct);
        }
        else
        {
            existing = new DayRating
            {
                UserId = userId,
                LocalDate = localDate,
                Rating = request.Rating,
                CreatedAt = DateTime.UtcNow
            };
            existing = await _ratingRepo.CreateAsync(existing, ct);
        }

        _logger.LogInformation("Day rating set: user_id={UserId}, date={Date}, rating={Rating}",
            userId, localDate, request.Rating);

        return Ok(new DayRatingResponse
        {
            LocalDate = localDate.ToString("yyyy-MM-dd"),
            Rating = existing.Rating,
            UpdatedAt = existing.UpdatedAt
        });
    }
}

public class SetDayRatingRequest
{
    public int Rating { get; set; }
    public string? LocalDate { get; set; }
}

public class DayRatingResponse
{
    public string LocalDate { get; set; } = string.Empty;
    public int? Rating { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
