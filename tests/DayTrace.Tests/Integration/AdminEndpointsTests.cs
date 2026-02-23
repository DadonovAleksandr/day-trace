using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace DayTrace.Tests.Integration;

/// <summary>
/// Tests for admin CRUD endpoints: users, events, summaries, period-jobs, delivery-attempts.
/// All use operator or admin role (sufficient for all non-audit endpoints).
/// </summary>
[Collection("Postgres")]
public class AdminEndpointsTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DayTraceWebFactory _factory = null!;

    public AdminEndpointsTests(PostgresFixture pg) => _pg = pg;

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

    private HttpClient CreateAdminClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // ========== GET /admin/users ==========

    [Fact]
    public async Task AdminUsers_List_Returns200WithItemsArray()
    {
        // Create a regular test user so list is not empty
        await _factory.CreateTestUserAsync();

        var (token, _) = await _factory.CreateAdminUserAsync("admin");
        var client = CreateAdminClient(token);

        var response = await client.GetAsync("/admin/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("items").GetArrayLength() >= 1);
        Assert.True(body.GetProperty("total").GetInt32() >= 1);
    }

    // ========== GET /admin/users/{id} ==========

    [Fact]
    public async Task AdminUsers_GetById_ExistingUser_Returns200()
    {
        var (userId, _) = await _factory.CreateTestUserAsync();
        var (token, _) = await _factory.CreateAdminUserAsync("admin");
        var client = CreateAdminClient(token);

        var response = await client.GetAsync($"/admin/users/{userId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(userId, body.GetProperty("id").GetInt64());
    }

    [Fact]
    public async Task AdminUsers_GetById_NonexistentUser_Returns404()
    {
        var (token, _) = await _factory.CreateAdminUserAsync("admin");
        var client = CreateAdminClient(token);

        var response = await client.GetAsync("/admin/users/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ========== GET /admin/events ==========

    [Fact]
    public async Task AdminEvents_List_ReturnsOk_AsOperator()
    {
        var (token, _) = await _factory.CreateAdminUserAsync("operator");
        var client = CreateAdminClient(token);

        var response = await client.GetAsync("/admin/events");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("items", out _));
    }

    // ========== GET /admin/summaries ==========

    [Fact]
    public async Task AdminSummaries_List_ReturnsOk()
    {
        var (token, _) = await _factory.CreateAdminUserAsync("operator");
        var client = CreateAdminClient(token);

        var response = await client.GetAsync("/admin/summaries");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("items", out _));
    }


    // ========== GET /admin/delivery-attempts ==========

    [Fact]
    public async Task AdminDeliveryAttempts_List_ReturnsOk()
    {
        var (token, _) = await _factory.CreateAdminUserAsync("operator");
        var client = CreateAdminClient(token);

        var response = await client.GetAsync("/admin/delivery-attempts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("items", out _));
    }
}
