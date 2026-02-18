using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace DayTrace.Tests.Integration;

[Collection("Postgres")]
public class HealthEndpointTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DayTraceWebFactory _factory = null!;

    public HealthEndpointTests(PostgresFixture pg) => _pg = pg;

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

    [Fact]
    public async Task Health_ReturnsOk_WithHealthyStatus()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("healthy", body.GetProperty("status").GetString());
    }
}
