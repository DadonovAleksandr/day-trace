using System.Net;

namespace DayTrace.Tests.Integration;

/// <summary>
/// Tests for admin RBAC middleware: role hierarchy admin > operator > analyst.
/// Middleware rules (from GetRequiredRole):
///   /admin/auth/* → analyst (any authenticated admin)
///   /admin/metrics/* → analyst
///   /admin/audit* → admin
///   Everything else (/admin/users, /admin/events, /admin/summaries, etc.) → operator
/// </summary>
[Collection("Postgres")]
public class AdminRbacTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DayTraceWebFactory _factory = null!;

    public AdminRbacTests(PostgresFixture pg) => _pg = pg;

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

    // ========== /admin/users — requires operator+ ==========

    [Fact]
    public async Task AdminUsers_AnalystRole_Returns403()
    {
        var (token, _) = await _factory.CreateAdminUserAsync("analyst");
        var client = CreateAdminClient(token);

        var response = await client.GetAsync("/admin/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminUsers_OperatorRole_Returns200()
    {
        var (token, _) = await _factory.CreateAdminUserAsync("operator");
        var client = CreateAdminClient(token);

        var response = await client.GetAsync("/admin/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ========== /admin/audit-logs — requires admin ==========

    [Fact]
    public async Task AdminAuditLogs_OperatorRole_Returns403()
    {
        var (token, _) = await _factory.CreateAdminUserAsync("operator");
        var client = CreateAdminClient(token);

        var response = await client.GetAsync("/admin/audit-logs");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminAuditLogs_AdminRole_Returns200()
    {
        var (token, _) = await _factory.CreateAdminUserAsync("admin");
        var client = CreateAdminClient(token);

        var response = await client.GetAsync("/admin/audit-logs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ========== /admin/metrics/dashboard — requires analyst+ ==========

    [Fact]
    public async Task AdminMetrics_AnalystRole_Returns200()
    {
        var (token, _) = await _factory.CreateAdminUserAsync("analyst");
        var client = CreateAdminClient(token);

        var response = await client.GetAsync("/admin/metrics/dashboard");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ========== /admin/events — requires operator+ (analyst blocked by middleware) ==========

    [Fact]
    public async Task AdminEvents_AnalystRole_Returns403()
    {
        var (token, _) = await _factory.CreateAdminUserAsync("analyst");
        var client = CreateAdminClient(token);

        var response = await client.GetAsync("/admin/events");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ========== /admin/summaries — requires operator+ (analyst blocked by middleware) ==========

    [Fact]
    public async Task AdminSummaries_AnalystRole_Returns403()
    {
        var (token, _) = await _factory.CreateAdminUserAsync("analyst");
        var client = CreateAdminClient(token);

        var response = await client.GetAsync("/admin/summaries");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminSummaries_OperatorRole_Returns200()
    {
        var (token, _) = await _factory.CreateAdminUserAsync("operator");
        var client = CreateAdminClient(token);

        var response = await client.GetAsync("/admin/summaries");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
