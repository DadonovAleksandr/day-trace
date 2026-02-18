using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Services;
using DayTrace.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DayTrace.Tests.Integration;

/// <summary>
/// Edge-case tests for events: edit window, inactive user, cross-user isolation.
/// </summary>
[Collection("Postgres")]
public class EventEdgeCaseTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DayTraceWebFactory _factory = null!;

    public EventEdgeCaseTests(PostgresFixture pg) => _pg = pg;

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

    /// <summary>
    /// Creates an event directly in the DB with a specific CreatedAt time.
    /// </summary>
    private async Task<Guid> CreateEventInDbAsync(long userId, DateTime createdAt)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DayTraceDbContext>();

        var evt = new Event
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Text = "Test event",
            Importance = 3,
            LocalDate = DateOnly.FromDateTime(createdAt),
            CreatedAt = createdAt
        };
        db.Events.Add(evt);
        await db.SaveChangesAsync();
        return evt.Id;
    }

    // ========== Edit window expired (168h) ==========

    [Fact]
    public async Task PatchEvent_OlderThan168h_Returns422_EditWindowExpired()
    {
        var (client, userId) = await _factory.CreateAuthenticatedClientAsync();

        // Create an event older than 168h (8 days ago)
        var eventId = await CreateEventInDbAsync(userId, DateTime.UtcNow.AddHours(-169));

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/events/{eventId}")
        {
            Content = JsonContent.Create(new { text = "Updated" })
        };
        request.Headers.Add("X-Client-Operation-Id", Guid.NewGuid().ToString());
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("edit_window_expired", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task DeleteEvent_OlderThan168h_Returns422_EditWindowExpired()
    {
        var (client, userId) = await _factory.CreateAuthenticatedClientAsync();

        // Create an event older than 168h (8 days ago)
        var eventId = await CreateEventInDbAsync(userId, DateTime.UtcNow.AddHours(-169));

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/events/{eventId}");
        request.Headers.Add("X-Client-Operation-Id", Guid.NewGuid().ToString());
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("edit_window_expired", body.GetProperty("error").GetString());
    }

    // ========== Inactive user ==========

    [Fact]
    public async Task InactiveUser_Returns403_AccountInactive()
    {
        var (userId, rawToken) = await _factory.CreateTestUserAsync();

        // Set user status to inactive
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DayTraceDbContext>();
            var user = await db.Users.FindAsync(userId);
            user!.Status = "suspended";
            await db.SaveChangesAsync();
        }

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", rawToken);

        var response = await client.GetAsync("/events");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("account_inactive", body.GetProperty("error").GetString());
    }

    // ========== Cross-user isolation ==========

    [Fact]
    public async Task CrossUserIsolation_UserA_CannotSee_UserB_Events()
    {
        var (clientA, userIdA) = await _factory.CreateAuthenticatedClientAsync();
        var (clientB, userIdB) = await _factory.CreateAuthenticatedClientAsync();

        // User B creates an event
        var eventId = await CreateEventInDbAsync(userIdB, DateTime.UtcNow);

        // User A tries to PATCH user B's event → should get 404 (not found for this user)
        var patchRequest = new HttpRequestMessage(HttpMethod.Patch, $"/events/{eventId}")
        {
            Content = JsonContent.Create(new { text = "Hacked!" })
        };
        patchRequest.Headers.Add("X-Client-Operation-Id", Guid.NewGuid().ToString());
        var patchResponse = await clientA.SendAsync(patchRequest);
        Assert.Equal(HttpStatusCode.NotFound, patchResponse.StatusCode);

        // User A tries to DELETE user B's event → should get 404
        var deleteRequest = new HttpRequestMessage(HttpMethod.Delete, $"/events/{eventId}");
        deleteRequest.Headers.Add("X-Client-Operation-Id", Guid.NewGuid().ToString());
        var deleteResponse = await clientA.SendAsync(deleteRequest);
        Assert.Equal(HttpStatusCode.NotFound, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task CrossUserIsolation_UserA_CannotSee_UserB_InListEvents()
    {
        var (clientA, userIdA) = await _factory.CreateAuthenticatedClientAsync();
        var (clientB, userIdB) = await _factory.CreateAuthenticatedClientAsync();

        // User B creates an event for today
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await CreateEventInDbAsync(userIdB, DateTime.UtcNow);

        // User A lists events for today — should not see user B's events
        var response = await clientA.GetAsync($"/events?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items");

        // User A should have no events (they didn't create any)
        Assert.Equal(0, items.GetArrayLength());
    }
}
