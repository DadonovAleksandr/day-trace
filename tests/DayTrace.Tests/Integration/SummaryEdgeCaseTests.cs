using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace DayTrace.Tests.Integration;

/// <summary>
/// Edge-case tests for summary endpoints: validation, pagination, period type checks.
/// </summary>
[Collection("Postgres")]
public class SummaryEdgeCaseTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DayTraceWebFactory _factory = null!;

    public SummaryEdgeCaseTests(PostgresFixture pg) => _pg = pg;

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

    // ========== GET /summaries/weekly — pagination + cursor ==========

    [Fact]
    public async Task ListSummaries_Weekly_ReturnsPaginatedResult()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/summaries/weekly?limit=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("items", out var items));
        Assert.True(body.TryGetProperty("next_cursor", out _));
    }

    // ========== GET /summaries/invalid — invalid period type ==========

    [Fact]
    public async Task ListSummaries_InvalidPeriodType_Returns400()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/summaries/invalid");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_period", body.GetProperty("error").GetString());
    }
}
