using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace DayTrace.Tests.Integration;

[Collection("Postgres")]
public class AdminAuthTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DayTraceWebFactory _factory = null!;

    public AdminAuthTests(PostgresFixture pg) => _pg = pg;

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
    public async Task AdminLogin_ValidCredentials_Returns200WithToken()
    {
        var (email, password, _) = await _factory.CreateAdminUserWithCredentialsAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/admin/auth/login", new { email, password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("token").GetString()!.Length > 0);
        Assert.Equal("admin", body.GetProperty("role").GetString());
        Assert.Equal(email, body.GetProperty("email").GetString());
    }

    [Fact]
    public async Task AdminLogin_InvalidPassword_Returns401()
    {
        var (email, _, _) = await _factory.CreateAdminUserWithCredentialsAsync();
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/admin/auth/login", new { email, password = "WrongPassword!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminLogin_MissingFields_Returns400()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/admin/auth/login", new { email = "", password = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_error", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task AdminLogout_ValidToken_Returns200()
    {
        var (token, _) = await _factory.CreateAdminUserAsync();
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync("/admin/auth/logout", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Logged out", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task AdminLogout_NoToken_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsync("/admin/auth/logout", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
