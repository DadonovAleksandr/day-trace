using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DayTrace.Domain.Entities;
using DayTrace.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;

namespace DayTrace.Tests.Integration;

/// <summary>
/// Integration tests for PUT /summaries/{periodType}/highlight — manual highlight event selection.
/// Replaces the old SummaryGenerationTests (PeriodJob auto-triggers).
/// </summary>
[Collection("Postgres")]
public class HighlightSelectionTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DayTraceWebFactory _factory = null!;

    public HighlightSelectionTests(PostgresFixture pg) => _pg = pg;

    public async Task InitializeAsync()
    {
        _factory = new DayTraceWebFactory(_pg.ConnectionString);
        await _factory.CleanDatabaseAsync();
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    // ========== Helpers ==========

    /// <summary>
    /// Creates an event via POST /events and returns (eventId, localDate).
    /// </summary>
    private async Task<(Guid EventId, DateOnly LocalDate)> CreateEventAsync(
        HttpClient client, string text = "Test event", int importance = 3, DateOnly? localDate = null)
    {
        var body = localDate.HasValue
            ? new { text, importance, local_date = localDate.Value.ToString("yyyy-MM-dd") }
            : (object)new { text, importance };

        var request = new HttpRequestMessage(HttpMethod.Post, "/events")
        {
            Content = JsonContent.Create(body),
            Headers = { { "X-Client-Operation-Id", Guid.NewGuid().ToString() } }
        };

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var eventId = json.GetProperty("id").GetGuid();
        var dateStr = json.GetProperty("local_date").GetString()!;
        var date = DateOnly.ParseExact(dateStr, "yyyy-MM-dd");
        return (eventId, date);
    }

    /// <summary>
    /// Computes the week boundaries (Monday–Sunday) for a given date,
    /// matching the default user week_end=Sunday setting.
    /// </summary>
    private static (DateOnly WeekStart, DateOnly WeekEnd) GetWeekBoundaries(DateOnly date)
    {
        // Default week_end = Sunday; week is Mon–Sun
        var weekEnd = date;
        while (weekEnd.DayOfWeek != DayOfWeek.Sunday)
            weekEnd = weekEnd.AddDays(1);
        var weekStart = weekEnd.AddDays(-6);
        return (weekStart, weekEnd);
    }

    /// <summary>
    /// Calls PUT /summaries/{periodType}/highlight with the given parameters.
    /// </summary>
    private static async Task<HttpResponseMessage> SetHighlightAsync(
        HttpClient client, string periodType, Guid eventId, DateOnly periodStart, DateOnly periodEnd)
    {
        var request = new HttpRequestMessage(HttpMethod.Put, $"/summaries/{periodType}/highlight")
        {
            Content = JsonContent.Create(new
            {
                event_id = eventId,
                period_start = periodStart.ToString("yyyy-MM-dd"),
                period_end = periodEnd.ToString("yyyy-MM-dd")
            }),
            Headers = { { "X-Client-Operation-Id", Guid.NewGuid().ToString() } }
        };
        return await client.SendAsync(request);
    }

    // ========== Tests ==========

    [Fact]
    public async Task SetHighlight_Success()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        // Create an event for today
        var (eventId, eventDate) = await CreateEventAsync(client);
        var (weekStart, weekEnd) = GetWeekBoundaries(eventDate);

        // Set highlight
        var response = await SetHighlightAsync(client, "weekly", eventId, weekStart, weekEnd);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("weekly", body.GetProperty("period_type").GetString());
        Assert.Equal(weekStart.ToString("yyyy-MM-dd"), body.GetProperty("period_start").GetString());
        Assert.Equal(weekEnd.ToString("yyyy-MM-dd"), body.GetProperty("period_end").GetString());
        Assert.Equal(eventId, body.GetProperty("highlight_event_id").GetGuid());
        Assert.Equal("generated", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task SetHighlight_EventNotFound_Returns404()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (weekStart, weekEnd) = GetWeekBoundaries(today);
        var nonExistentEventId = Guid.NewGuid();

        var response = await SetHighlightAsync(client, "weekly", nonExistentEventId, weekStart, weekEnd);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("event_not_found", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task SetHighlight_EventOutsidePeriod_Returns400()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        // Create an event for today
        var (eventId, eventDate) = await CreateEventAsync(client);

        // Build a period range that does NOT include the event's date (previous week)
        var (weekStart, weekEnd) = GetWeekBoundaries(eventDate);
        var previousWeekStart = weekStart.AddDays(-7);
        var previousWeekEnd = weekEnd.AddDays(-7);

        var response = await SetHighlightAsync(client, "weekly", eventId, previousWeekStart, previousWeekEnd);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("event_outside_period", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task SetHighlight_InvalidPeriodType_Returns400()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        var (eventId, eventDate) = await CreateEventAsync(client);
        var (weekStart, weekEnd) = GetWeekBoundaries(eventDate);

        var response = await SetHighlightAsync(client, "invalid", eventId, weekStart, weekEnd);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_period", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task SetHighlight_ChangeHighlight_Success()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        // Pick a Wednesday within the backdate window (guaranteed mid-week: both -1 and +0 stay within Mon–Sun)
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var target = today;
        // Walk backward to the nearest Wednesday (still within 30-day backdate window)
        while (target.DayOfWeek != DayOfWeek.Wednesday && target > today.AddDays(-25))
            target = target.AddDays(-1);

        // Create two events on consecutive days within the same week
        var date1 = target;
        var date2 = target.AddDays(-1); // Tuesday of the same week

        var (eventId1, eventDate1) = await CreateEventAsync(client, "First event", localDate: date1);
        var (eventId2, _) = await CreateEventAsync(client, "Second event", localDate: date2);

        var (weekStart, weekEnd) = GetWeekBoundaries(eventDate1);

        // Set highlight to first event
        var response1 = await SetHighlightAsync(client, "weekly", eventId1, weekStart, weekEnd);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        var body1 = await response1.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(eventId1, body1.GetProperty("highlight_event_id").GetGuid());

        // Change highlight to second event
        var response2 = await SetHighlightAsync(client, "weekly", eventId2, weekStart, weekEnd);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        var body2 = await response2.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(eventId2, body2.GetProperty("highlight_event_id").GetGuid());
    }

    [Fact]
    public async Task SetHighlight_Locked_Returns422()
    {
        var (client, userId) = await _factory.CreateAuthenticatedClientAsync();

        // Create an event for today
        var (eventId, eventDate) = await CreateEventAsync(client);
        var (weekStart, weekEnd) = GetWeekBoundaries(eventDate);

        // Insert a monthly summary with status="generated" that covers this week's month
        // This will lock the weekly highlight via EventLockService.IsSummaryLockedAsync
        var monthStart = new DateOnly(eventDate.Year, eventDate.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DayTraceDbContext>();
            var monthlySummary = new Summary
            {
                UserId = userId,
                PeriodType = "monthly",
                PeriodStart = monthStart,
                PeriodEnd = monthEnd,
                Status = "generated",
                Version = 1,
                LastGeneratedAt = DateTime.UtcNow
            };
            db.Summaries.Add(monthlySummary);
            await db.SaveChangesAsync();
        }

        // Try to set weekly highlight — should be locked by monthly
        var response = await SetHighlightAsync(client, "weekly", eventId, weekStart, weekEnd);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("locked_by_summary", body.GetProperty("error").GetString());
        Assert.Equal("monthly", body.GetProperty("locked_by").GetString());
    }

    [Fact]
    public async Task ListSummaries_IncludesHighlightEventId()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        // Create event and set highlight
        var (eventId, eventDate) = await CreateEventAsync(client);
        var (weekStart, weekEnd) = GetWeekBoundaries(eventDate);

        var highlightResponse = await SetHighlightAsync(client, "weekly", eventId, weekStart, weekEnd);
        Assert.Equal(HttpStatusCode.OK, highlightResponse.StatusCode);

        // List weekly summaries
        var listResponse = await client.GetAsync("/summaries/weekly");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var body = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("items", out var items));

        var itemArray = items.EnumerateArray().ToArray();
        Assert.Single(itemArray);

        var item = itemArray[0];
        Assert.Equal(eventId, item.GetProperty("highlight_event_id").GetGuid());
        Assert.Equal("weekly", item.GetProperty("period_type").GetString());
        Assert.Equal(weekStart.ToString("yyyy-MM-dd"), item.GetProperty("period_start").GetString());
        Assert.Equal(weekEnd.ToString("yyyy-MM-dd"), item.GetProperty("period_end").GetString());
    }
}
