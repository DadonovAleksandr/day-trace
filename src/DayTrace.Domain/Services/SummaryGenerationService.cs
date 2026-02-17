using System.Text.Json;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;

namespace DayTrace.Domain.Services;

/// <summary>
/// Generates summary content as structured JSON with sorted events (US-026, FR-14.1).
/// No AI summarization in MVP — just structured aggregation.
/// </summary>
public class SummaryGenerationService
{
    private readonly IEventRepository _eventRepo;
    private readonly IDomainLogger _logger;

    public SummaryGenerationService(
        IEventRepository eventRepo,
        IDomainLogger logger)
    {
        _eventRepo = eventRepo;
        _logger = logger;
    }

    /// <summary>
    /// Generates summary content for a period. Returns content JSON and source event IDs.
    /// </summary>
    public async Task<(string ContentJson, Guid[] SourceEventIds)> GenerateAsync(
        long userId, DateOnly periodStart, DateOnly periodEnd,
        CancellationToken ct = default)
    {
        // Select non-deleted events in [period_start, period_end]
        var events = await _eventRepo.GetByPeriodAsync(userId, periodStart, periodEnd, ct);

        // Sort: local_date ASC, importance DESC, created_at ASC
        var sorted = events
            .OrderBy(e => e.LocalDate)
            .ThenByDescending(e => e.Importance)
            .ThenBy(e => e.CreatedAt)
            .ToList();

        var sourceEventIds = sorted.Select(e => e.Id).ToArray();

        var content = new SummaryContent
        {
            Events = sorted.Select(e => new SummaryEventItem
            {
                EventId = e.Id,
                Text = e.Text,
                Importance = e.Importance,
                LocalDate = e.LocalDate.ToString("yyyy-MM-dd")
            }).ToList(),
            TotalEvents = sorted.Count,
            PeriodStart = periodStart.ToString("yyyy-MM-dd"),
            PeriodEnd = periodEnd.ToString("yyyy-MM-dd")
        };

        var contentJson = JsonSerializer.Serialize(content, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false
        });

        _logger.Info("Summary generated: user_id={UserId}, period=[{Start}..{End}], events={Count}",
            userId, periodStart.ToString("yyyy-MM-dd"), periodEnd.ToString("yyyy-MM-dd"), sorted.Count);

        return (contentJson, sourceEventIds);
    }
}

public class SummaryContent
{
    public List<SummaryEventItem> Events { get; set; } = new();
    public int TotalEvents { get; set; }
    public string PeriodStart { get; set; } = string.Empty;
    public string PeriodEnd { get; set; } = string.Empty;
}

public class SummaryEventItem
{
    public Guid EventId { get; set; }
    public string Text { get; set; } = string.Empty;
    public int Importance { get; set; }
    public string LocalDate { get; set; } = string.Empty;
}
