using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace DayTrace.Tests.Integration;

/// <summary>
/// US-064: Integration tests — event lifecycle (create, read, edit, delete).
/// Tests run against real PostgreSQL via Testcontainers.
/// </summary>
[Collection("Postgres")]
public class EventLifecycleTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DayTraceWebFactory _factory = null!;

    public EventLifecycleTests(PostgresFixture pg) => _pg = pg;

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

    // ========== Create Event ==========

    [Fact]
    public async Task CreateEvent_ValidData_Returns201()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();
        var operationId = Guid.NewGuid().ToString();

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/events")
        {
            Content = JsonContent.Create(new { text = "Test event", importance = 3 }),
            Headers = { { "X-Client-Operation-Id", operationId } }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Test event", body.GetProperty("text").GetString());
        Assert.Equal(3, body.GetProperty("importance").GetInt32());
        Assert.NotEqual(Guid.Empty.ToString(), body.GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("", 3, "validation_error")] // empty text
    [InlineData(null, 3, "validation_error")] // null text
    [InlineData("Valid text", 0, "validation_error")] // importance < 1
    [InlineData("Valid text", 6, "validation_error")] // importance > 5
    public async Task CreateEvent_InvalidData_Returns400(string? text, int importance, string expectedError)
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/events")
        {
            Content = JsonContent.Create(new { text = text ?? "", importance }),
            Headers = { { "X-Client-Operation-Id", Guid.NewGuid().ToString() } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expectedError, body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task CreateEvent_TextTooLong_Returns400()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();
        var longText = new string('A', 501);

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/events")
        {
            Content = JsonContent.Create(new { text = longText, importance = 3 }),
            Headers = { { "X-Client-Operation-Id", Guid.NewGuid().ToString() } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_DateOutOfRange_Returns400()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();
        var oldDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-60).ToString("yyyy-MM-dd");

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/events")
        {
            Content = JsonContent.Create(new { text = "Old event", importance = 2, local_date = oldDate }),
            Headers = { { "X-Client-Operation-Id", Guid.NewGuid().ToString() } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("date_out_of_range", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task CreateEvent_BackdateWithin30Days_Returns201()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1).ToString("yyyy-MM-dd");

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/events")
        {
            Content = JsonContent.Create(new { text = "Yesterday event", importance = 4, local_date = yesterday }),
            Headers = { { "X-Client-Operation-Id", Guid.NewGuid().ToString() } }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(yesterday, body.GetProperty("local_date").GetString());
    }

    // ========== Edit Event ==========

    [Fact]
    public async Task EditEvent_Within7Days_Succeeds()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        // Create event
        var createResponse = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/events")
        {
            Content = JsonContent.Create(new { text = "Original", importance = 2 }),
            Headers = { { "X-Client-Operation-Id", Guid.NewGuid().ToString() } }
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var eventId = created.GetProperty("id").GetString();

        // Edit
        var editResponse = await client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/events/{eventId}")
        {
            Content = JsonContent.Create(new { text = "Edited text", importance = 5 }),
            Headers = { { "X-Client-Operation-Id", Guid.NewGuid().ToString() } }
        });

        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
        var edited = await editResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Edited text", edited.GetProperty("text").GetString());
        Assert.Equal(5, edited.GetProperty("importance").GetInt32());
    }

    [Fact]
    public async Task EditEvent_PartialUpdate_OnlyUpdatesProvidedFields()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        var createResponse = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/events")
        {
            Content = JsonContent.Create(new { text = "Original text", importance = 3 }),
            Headers = { { "X-Client-Operation-Id", Guid.NewGuid().ToString() } }
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var eventId = created.GetProperty("id").GetString();

        // Update only importance
        var editResponse = await client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/events/{eventId}")
        {
            Content = JsonContent.Create(new { importance = 1 }),
            Headers = { { "X-Client-Operation-Id", Guid.NewGuid().ToString() } }
        });

        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
        var edited = await editResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Original text", edited.GetProperty("text").GetString()); // unchanged
        Assert.Equal(1, edited.GetProperty("importance").GetInt32()); // changed
    }

    [Fact]
    public async Task EditEvent_NonExistent_Returns404()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();
        var fakeId = Guid.NewGuid();

        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, $"/events/{fakeId}")
        {
            Content = JsonContent.Create(new { text = "Won't work" }),
            Headers = { { "X-Client-Operation-Id", Guid.NewGuid().ToString() } }
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ========== Delete Event ==========

    [Fact]
    public async Task DeleteEvent_SoftDeleteExcludesFromQueries()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        // Create event
        var createResponse = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/events")
        {
            Content = JsonContent.Create(new { text = "To be deleted", importance = 1 }),
            Headers = { { "X-Client-Operation-Id", Guid.NewGuid().ToString() } }
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var eventId = created.GetProperty("id").GetString();
        var localDate = created.GetProperty("local_date").GetString();

        // Delete
        var deleteResponse = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/events/{eventId}")
        {
            Headers = { { "X-Client-Operation-Id", Guid.NewGuid().ToString() } }
        });
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Verify excluded from GET /events
        var listResponse = await client.GetFromJsonAsync<JsonElement>($"/events?from={localDate}&to={localDate}");
        var items = listResponse.GetProperty("items");
        foreach (var item in items.EnumerateArray())
        {
            Assert.NotEqual(eventId, item.GetProperty("id").GetString());
        }
    }

    [Fact]
    public async Task DeleteEvent_AlreadyDeleted_Returns404()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        var createResponse = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/events")
        {
            Content = JsonContent.Create(new { text = "Delete twice", importance = 2 }),
            Headers = { { "X-Client-Operation-Id", Guid.NewGuid().ToString() } }
        });
        var created = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        var eventId = created.GetProperty("id").GetString();

        // Delete first time
        await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/events/{eventId}")
        {
            Headers = { { "X-Client-Operation-Id", Guid.NewGuid().ToString() } }
        });

        // Delete second time
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/events/{eventId}")
        {
            Headers = { { "X-Client-Operation-Id", Guid.NewGuid().ToString() } }
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ========== Client Operation ID Dedupe ==========

    [Fact]
    public async Task CreateEvent_DuplicateOperationId_ReturnsIdempotent()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();
        var operationId = Guid.NewGuid().ToString();

        var response1 = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/events")
        {
            Content = JsonContent.Create(new { text = "Dedupe test", importance = 3 }),
            Headers = { { "X-Client-Operation-Id", operationId } }
        });
        Assert.Equal(HttpStatusCode.Created, response1.StatusCode);

        // Same operation ID = idempotent (returns original status code 201, not 200)
        var response2 = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/events")
        {
            Content = JsonContent.Create(new { text = "Dedupe test", importance = 3 }),
            Headers = { { "X-Client-Operation-Id", operationId } }
        });
        Assert.Equal(HttpStatusCode.Created, response2.StatusCode);
    }

    [Fact]
    public async Task CreateEvent_MissingOperationId_Returns400()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        // POST without X-Client-Operation-Id header
        var response = await client.PostAsJsonAsync("/events", new { text = "No operation id", importance = 2 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ========== List Events ==========

    [Fact]
    public async Task ListEvents_DefaultsToToday()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        // Create event for today
        await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/events")
        {
            Content = JsonContent.Create(new { text = "Today's event", importance = 3 }),
            Headers = { { "X-Client-Operation-Id", Guid.NewGuid().ToString() } }
        });

        var response = await client.GetFromJsonAsync<JsonElement>("/events");
        var items = response.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task ListEvents_FilterByDateRange()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

        // Create event
        await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/events")
        {
            Content = JsonContent.Create(new { text = "Range test", importance = 2 }),
            Headers = { { "X-Client-Operation-Id", Guid.NewGuid().ToString() } }
        });

        // Query with date range
        var response = await client.GetFromJsonAsync<JsonElement>($"/events?from={today}&to={today}");
        var items = response.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);

        // Query future date range — should be empty
        var futureDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(5).ToString("yyyy-MM-dd");
        var futureResponse = await client.GetFromJsonAsync<JsonElement>($"/events?from={futureDate}&to={futureDate}");
        Assert.Equal(0, futureResponse.GetProperty("items").GetArrayLength());
    }
}
