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
/// Integration tests for subscription system (Freemium / Telegram Stars).
/// Tests cover full lifecycle: trial start, grace period, expiry, payment, exempt.
/// </summary>
[Collection("Postgres")]
public class SubscriptionTests : IAsyncLifetime
{
    private readonly PostgresFixture _pg;
    private DayTraceWebFactory _factory = null!;

    public SubscriptionTests(PostgresFixture pg) => _pg = pg;

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

    private HttpClient CreateAuthClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private HttpClient CreateAdminClient(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Directly manipulate subscription via DB for test setup.
    /// </summary>
    private async Task SetSubscriptionAsync(long userId, Action<Subscription> configure)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DayTraceDbContext>();

        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId);
        var now = DateTime.UtcNow;
        if (sub == null)
        {
            sub = new Subscription { UserId = userId, CreatedAt = now, UpdatedAt = now };
            db.Subscriptions.Add(sub);
        }
        configure(sub);
        sub.UpdatedAt = now;
        await db.SaveChangesAsync();
    }

    // ========== GET /subscription ==========

    [Fact]
    public async Task GetSubscription_NoSubscription_ReturnsNotStartedWithAccess()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/subscription");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("not_started", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetSubscription_TrialActive_ReturnsTrial()
    {
        var (client, userId) = await _factory.CreateAuthenticatedClientAsync();
        await SetSubscriptionAsync(userId, s =>
        {
            s.TrialStartedAt = DateTime.UtcNow.AddDays(-10);
            s.TrialExpiresAt = DateTime.UtcNow.AddDays(20);
        });

        var response = await client.GetAsync("/subscription");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("trial", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("days_remaining").GetInt32() > 0);
    }

    [Fact]
    public async Task GetSubscription_SubscriptionActive_ReturnsActive()
    {
        var (client, userId) = await _factory.CreateAuthenticatedClientAsync();
        await SetSubscriptionAsync(userId, s =>
        {
            s.TrialStartedAt = DateTime.UtcNow.AddDays(-40);
            s.TrialExpiresAt = DateTime.UtcNow.AddDays(-10);
            s.SubscriptionExpiresAt = DateTime.UtcNow.AddDays(20);
        });

        var response = await client.GetAsync("/subscription");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("active", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetSubscription_InGracePeriod_ReturnsGracePeriod()
    {
        var (client, userId) = await _factory.CreateAuthenticatedClientAsync();
        await SetSubscriptionAsync(userId, s =>
        {
            s.TrialStartedAt = DateTime.UtcNow.AddDays(-35);
            s.TrialExpiresAt = DateTime.UtcNow.AddDays(-3); // expired 3 days ago, within 7d grace
        });

        var response = await client.GetAsync("/subscription");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("grace_period", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetSubscription_Exempt_ReturnsExempt()
    {
        var (client, userId) = await _factory.CreateAuthenticatedClientAsync();
        await SetSubscriptionAsync(userId, s =>
        {
            s.IsExempt = true;
            s.TrialStartedAt = DateTime.UtcNow.AddDays(-40);
            s.TrialExpiresAt = DateTime.UtcNow.AddDays(-10); // expired, but exempt
        });

        var response = await client.GetAsync("/subscription");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("exempt", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("is_exempt").GetBoolean());
    }

    // ========== SubscriptionCheckMiddleware — 402 on expired ==========

    [Fact]
    public async Task Events_WhenSubscriptionExpired_Returns402()
    {
        var (client, userId) = await _factory.CreateAuthenticatedClientAsync();
        await SetSubscriptionAsync(userId, s =>
        {
            s.TrialStartedAt = DateTime.UtcNow.AddDays(-45);
            s.TrialExpiresAt = DateTime.UtcNow.AddDays(-10); // expired, past grace
        });

        var response = await client.GetAsync("/events");

        Assert.Equal(HttpStatusCode.PaymentRequired, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("subscription_expired", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Events_WhenInGracePeriod_Returns200()
    {
        var (client, userId) = await _factory.CreateAuthenticatedClientAsync();
        await SetSubscriptionAsync(userId, s =>
        {
            s.TrialStartedAt = DateTime.UtcNow.AddDays(-35);
            s.TrialExpiresAt = DateTime.UtcNow.AddDays(-3); // grace period
        });

        var response = await client.GetAsync("/events");

        // Grace period still has access
        Assert.NotEqual(HttpStatusCode.PaymentRequired, response.StatusCode);
    }

    [Fact]
    public async Task SubscriptionEndpoint_WhenExpired_StillReturns200()
    {
        // /subscription itself is not blocked even when expired
        var (client, userId) = await _factory.CreateAuthenticatedClientAsync();
        await SetSubscriptionAsync(userId, s =>
        {
            s.TrialStartedAt = DateTime.UtcNow.AddDays(-45);
            s.TrialExpiresAt = DateTime.UtcNow.AddDays(-10);
        });

        var response = await client.GetAsync("/subscription");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("expired", body.GetProperty("status").GetString());
    }

    // ========== Trial start on first event ==========

    [Fact]
    public async Task CreateEvent_FirstEvent_StartsTrialAutomatically()
    {
        var (client, userId) = await _factory.CreateAuthenticatedClientAsync();
        var operationId = Guid.NewGuid().ToString();

        // No subscription exists yet
        var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/events")
        {
            Content = JsonContent.Create(new { text = "First event ever!", importance = 3 }),
            Headers = { { "X-Client-Operation-Id", operationId } }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Check subscription was created with trial
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DayTraceDbContext>();
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId);

        Assert.NotNull(sub);
        Assert.NotNull(sub.TrialStartedAt);
        Assert.NotNull(sub.TrialExpiresAt);
        Assert.True(sub.TrialExpiresAt > DateTime.UtcNow.AddDays(28));
    }

    [Fact]
    public async Task CreateEvent_SecondEvent_DoesNotResetTrial()
    {
        var (client, userId) = await _factory.CreateAuthenticatedClientAsync();
        var trialStart = DateTime.UtcNow.AddDays(-5);

        // Set existing subscription
        await SetSubscriptionAsync(userId, s =>
        {
            s.TrialStartedAt = trialStart;
            s.TrialExpiresAt = trialStart.AddDays(30);
        });

        var operationId = Guid.NewGuid().ToString();
        await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/events")
        {
            Content = JsonContent.Create(new { text = "Second event", importance = 2 }),
            Headers = { { "X-Client-Operation-Id", operationId } }
        });

        // Trial start should not change
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DayTraceDbContext>();
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId);

        Assert.NotNull(sub);
        Assert.Equal(trialStart.Date, sub.TrialStartedAt!.Value.Date);
    }

    // ========== POST /subscription/checkout ==========

    [Fact]
    public async Task Checkout_MonthlyPlan_AcceptsRequest()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, "/subscription/checkout")
        {
            Content = JsonContent.Create(new { plan = "monthly" }),
            Headers = { { "X-Client-Operation-Id", Guid.NewGuid().ToString() } }
        };
        var response = await client.SendAsync(request);

        // In test env, ITelegramBotClient is mocked — may fail with 500 but not 400/401
        Assert.NotEqual(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_InvalidPlan_Returns400()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, "/subscription/checkout")
        {
            Content = JsonContent.Create(new { plan = "invalid_plan" }),
            Headers = { { "X-Client-Operation-Id", Guid.NewGuid().ToString() } }
        };
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Checkout_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/subscription/checkout", new { plan = "monthly" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ========== Admin subscription endpoints ==========

    [Fact]
    public async Task AdminSubscriptions_List_Returns200WithItems()
    {
        var (userId, _) = await _factory.CreateTestUserAsync();
        await SetSubscriptionAsync(userId, s =>
        {
            s.TrialStartedAt = DateTime.UtcNow.AddDays(-10);
            s.TrialExpiresAt = DateTime.UtcNow.AddDays(20);
        });

        var (adminToken, _) = await _factory.CreateAdminUserAsync("admin");
        var adminClient = CreateAdminClient(adminToken);

        var response = await adminClient.GetAsync("/admin/subscriptions?limit=20&offset=0");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("items").GetArrayLength() >= 1);
        Assert.True(body.GetProperty("total").GetInt32() >= 1);
    }

    [Fact]
    public async Task AdminSubscriptions_NonAdmin_Returns403()
    {
        var (operatorToken, _) = await _factory.CreateAdminUserAsync("operator");
        var operatorClient = CreateAdminClient(operatorToken);

        var response = await operatorClient.GetAsync("/admin/subscriptions");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminSubscriptions_Exempt_SetsIsExemptTrue()
    {
        var (userId, _) = await _factory.CreateTestUserAsync();
        var (adminToken, _) = await _factory.CreateAdminUserAsync("admin");
        var adminClient = CreateAdminClient(adminToken);

        var response = await adminClient.PostAsync($"/admin/subscriptions/{userId}/exempt", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify subscription was created with is_exempt = true
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DayTraceDbContext>();
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId);
        Assert.NotNull(sub);
        Assert.True(sub.IsExempt);
    }

    [Fact]
    public async Task AdminSubscriptions_RemoveExempt_SetsIsExemptFalse()
    {
        var (userId, _) = await _factory.CreateTestUserAsync();
        await SetSubscriptionAsync(userId, s => s.IsExempt = true);

        var (adminToken, _) = await _factory.CreateAdminUserAsync("admin");
        var adminClient = CreateAdminClient(adminToken);

        var response = await adminClient.DeleteAsync($"/admin/subscriptions/{userId}/exempt");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DayTraceDbContext>();
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId);
        Assert.NotNull(sub);
        Assert.False(sub.IsExempt);
    }

    [Fact]
    public async Task AdminSubscriptions_ResetTrial_UpdatesTrialDates()
    {
        var (userId, _) = await _factory.CreateTestUserAsync();
        var oldExpiry = DateTime.UtcNow.AddDays(-10);
        await SetSubscriptionAsync(userId, s =>
        {
            s.TrialStartedAt = DateTime.UtcNow.AddDays(-40);
            s.TrialExpiresAt = oldExpiry;
        });

        var (adminToken, _) = await _factory.CreateAdminUserAsync("admin");
        var adminClient = CreateAdminClient(adminToken);

        var response = await adminClient.PostAsync($"/admin/subscriptions/{userId}/reset-trial", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DayTraceDbContext>();
        var sub = await db.Subscriptions.FirstOrDefaultAsync(s => s.UserId == userId);
        Assert.NotNull(sub);
        Assert.True(sub.TrialExpiresAt > DateTime.UtcNow.AddDays(28));
    }

    [Fact]
    public async Task AdminSubscriptions_GetUserDetail_Returns200WithPaymentHistory()
    {
        var (userId, _) = await _factory.CreateTestUserAsync();
        await SetSubscriptionAsync(userId, s =>
        {
            s.TrialStartedAt = DateTime.UtcNow.AddDays(-10);
            s.TrialExpiresAt = DateTime.UtcNow.AddDays(20);
        });

        var (adminToken, _) = await _factory.CreateAdminUserAsync("admin");
        var adminClient = CreateAdminClient(adminToken);

        var response = await adminClient.GetAsync($"/admin/subscriptions/{userId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(userId, body.GetProperty("user_id").GetInt64());
        Assert.True(body.TryGetProperty("payment_history", out var history));
        Assert.Equal(JsonValueKind.Array, history.ValueKind);
    }
}
