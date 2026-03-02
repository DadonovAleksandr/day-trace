using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Interfaces;
using DayTrace.Domain.Services;
using DayTrace.Infrastructure.Data;
using DayTrace.Infrastructure.Repositories;
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

    private async Task<HashSet<long>> GetAdminUserIdsByStatusAsync(HttpClient adminClient, string status)
    {
        var response = await adminClient.GetAsync($"/admin/subscriptions?limit=100&offset=0&status={status}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("items")
            .EnumerateArray()
            .Select(item => item.GetProperty("user_id").GetInt64())
            .ToHashSet();
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
    public async Task Checkout_MonthlyPlan_ReturnsInvoiceLink()
    {
        var (client, _) = await _factory.CreateAuthenticatedClientAsync();

        var request = new HttpRequestMessage(HttpMethod.Post, "/subscription/checkout")
        {
            Content = JsonContent.Create(new { plan = "monthly" }),
            Headers = { { "X-Client-Operation-Id", Guid.NewGuid().ToString() } }
        };
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("invoice_link", out var link));
        Assert.False(string.IsNullOrEmpty(link.GetString()));
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
    public async Task AdminSubscriptions_FilterNotStarted_ExcludesLegacyExpiredAndGrace()
    {
        var (notStartedUserId, _) = await _factory.CreateTestUserAsync();
        await SetSubscriptionAsync(notStartedUserId, s =>
        {
            s.TrialStartedAt = null;
            s.TrialExpiresAt = null;
            s.SubscriptionExpiresAt = null;
            s.IsExempt = false;
        });

        var (graceUserId, _) = await _factory.CreateTestUserAsync();
        await SetSubscriptionAsync(graceUserId, s =>
        {
            s.TrialStartedAt = null;
            s.TrialExpiresAt = null;
            s.SubscriptionExpiresAt = DateTime.UtcNow.AddDays(-3);
            s.IsExempt = false;
        });

        var (expiredUserId, _) = await _factory.CreateTestUserAsync();
        await SetSubscriptionAsync(expiredUserId, s =>
        {
            s.TrialStartedAt = null;
            s.TrialExpiresAt = null;
            s.SubscriptionExpiresAt = DateTime.UtcNow.AddDays(-12);
            s.IsExempt = false;
        });

        var (adminToken, _) = await _factory.CreateAdminUserAsync("admin");
        var adminClient = CreateAdminClient(adminToken);

        var userIds = await GetAdminUserIdsByStatusAsync(adminClient, "not_started");

        Assert.Contains(notStartedUserId, userIds);
        Assert.DoesNotContain(graceUserId, userIds);
        Assert.DoesNotContain(expiredUserId, userIds);
    }

    [Fact]
    public async Task AdminSubscriptions_FilterGracePeriod_IncludesTrialAndPaidGrace()
    {
        var (trialGraceUserId, _) = await _factory.CreateTestUserAsync();
        await SetSubscriptionAsync(trialGraceUserId, s =>
        {
            s.TrialStartedAt = DateTime.UtcNow.AddDays(-40);
            s.TrialExpiresAt = DateTime.UtcNow.AddDays(-2);
            s.SubscriptionExpiresAt = null;
            s.IsExempt = false;
        });

        var (paidGraceUserId, _) = await _factory.CreateTestUserAsync();
        await SetSubscriptionAsync(paidGraceUserId, s =>
        {
            s.TrialStartedAt = null;
            s.TrialExpiresAt = null;
            s.SubscriptionExpiresAt = DateTime.UtcNow.AddDays(-4);
            s.IsExempt = false;
        });

        var (expiredUserId, _) = await _factory.CreateTestUserAsync();
        await SetSubscriptionAsync(expiredUserId, s =>
        {
            s.TrialStartedAt = null;
            s.TrialExpiresAt = null;
            s.SubscriptionExpiresAt = DateTime.UtcNow.AddDays(-10);
            s.IsExempt = false;
        });

        var (adminToken, _) = await _factory.CreateAdminUserAsync("admin");
        var adminClient = CreateAdminClient(adminToken);

        var userIds = await GetAdminUserIdsByStatusAsync(adminClient, "grace_period");

        Assert.Contains(trialGraceUserId, userIds);
        Assert.Contains(paidGraceUserId, userIds);
        Assert.DoesNotContain(expiredUserId, userIds);
    }

    [Fact]
    public async Task AdminSubscriptions_FilterExpired_MatchesServiceExpiredCases()
    {
        var (trialExpiredUserId, _) = await _factory.CreateTestUserAsync();
        await SetSubscriptionAsync(trialExpiredUserId, s =>
        {
            s.TrialStartedAt = DateTime.UtcNow.AddDays(-50);
            s.TrialExpiresAt = DateTime.UtcNow.AddDays(-20);
            s.SubscriptionExpiresAt = null;
            s.IsExempt = false;
        });

        var (paidExpiredUserId, _) = await _factory.CreateTestUserAsync();
        await SetSubscriptionAsync(paidExpiredUserId, s =>
        {
            s.TrialStartedAt = null;
            s.TrialExpiresAt = null;
            s.SubscriptionExpiresAt = DateTime.UtcNow.AddDays(-12);
            s.IsExempt = false;
        });

        var (startedNoDatesUserId, _) = await _factory.CreateTestUserAsync();
        await SetSubscriptionAsync(startedNoDatesUserId, s =>
        {
            s.TrialStartedAt = DateTime.UtcNow.AddDays(-5);
            s.TrialExpiresAt = null;
            s.SubscriptionExpiresAt = null;
            s.IsExempt = false;
        });

        var (graceUserId, _) = await _factory.CreateTestUserAsync();
        await SetSubscriptionAsync(graceUserId, s =>
        {
            s.TrialStartedAt = null;
            s.TrialExpiresAt = null;
            s.SubscriptionExpiresAt = DateTime.UtcNow.AddDays(-2);
            s.IsExempt = false;
        });

        var (adminToken, _) = await _factory.CreateAdminUserAsync("admin");
        var adminClient = CreateAdminClient(adminToken);

        var userIds = await GetAdminUserIdsByStatusAsync(adminClient, "expired");

        Assert.Contains(trialExpiredUserId, userIds);
        Assert.Contains(paidExpiredUserId, userIds);
        Assert.Contains(startedNoDatesUserId, userIds);
        Assert.DoesNotContain(graceUserId, userIds);
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

    [Fact]
    public async Task ActivateAsync_DuplicateChargeId_IsIdempotent()
    {
        var (userId, _) = await _factory.CreateTestUserAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DayTraceDbContext>();
        var txExecutor = scope.ServiceProvider.GetRequiredService<ITransactionExecutor>();
        var service = new SubscriptionService(
            new SubscriptionRepository(db),
            new StarPaymentRepository(db),
            txExecutor);

        var firstActivation = await service.ActivateAsync(userId, "monthly", "charge_idempotent_1");
        var afterFirst = await db.Subscriptions.AsNoTracking().FirstAsync(s => s.UserId == userId);

        var secondActivation = await service.ActivateAsync(userId, "monthly", "charge_idempotent_1");
        var afterSecond = await db.Subscriptions.AsNoTracking().FirstAsync(s => s.UserId == userId);
        var paymentCount = await db.StarPayments.CountAsync(p => p.TelegramPaymentChargeId == "charge_idempotent_1");

        Assert.True(firstActivation);
        Assert.False(secondActivation);
        Assert.Equal(1, paymentCount);
        Assert.Equal(afterFirst.SubscriptionExpiresAt, afterSecond.SubscriptionExpiresAt);
    }

    [Fact]
    public async Task ActivateAsync_WhenSubscriptionSaveFails_RollsBackPayment()
    {
        var (userId, _) = await _factory.CreateTestUserAsync();
        var originalExpiry = DateTime.UtcNow.AddDays(-1);
        await SetSubscriptionAsync(userId, s =>
        {
            s.SubscriptionExpiresAt = originalExpiry;
            s.TrialStartedAt = null;
            s.TrialExpiresAt = null;
            s.IsExempt = false;
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DayTraceDbContext>();
        var txExecutor = scope.ServiceProvider.GetRequiredService<ITransactionExecutor>();
        var failingRepo = new ThrowOnUpdateSubscriptionRepository(new SubscriptionRepository(db));
        var service = new SubscriptionService(
            failingRepo,
            new StarPaymentRepository(db),
            txExecutor);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ActivateAsync(userId, "monthly", "charge_tx_rollback_1"));

        db.ChangeTracker.Clear();
        var payment = await db.StarPayments
            .SingleOrDefaultAsync(p => p.TelegramPaymentChargeId == "charge_tx_rollback_1");
        var subscription = await db.Subscriptions
            .AsNoTracking()
            .SingleAsync(s => s.UserId == userId);

        Assert.Null(payment);
        Assert.NotNull(subscription.SubscriptionExpiresAt);
        Assert.Equal(originalExpiry, subscription.SubscriptionExpiresAt.Value, TimeSpan.FromMilliseconds(1));
    }

    private sealed class ThrowOnUpdateSubscriptionRepository : ISubscriptionRepository
    {
        private readonly ISubscriptionRepository _inner;

        public ThrowOnUpdateSubscriptionRepository(ISubscriptionRepository inner)
        {
            _inner = inner;
        }

        public Task<Subscription?> GetByUserIdAsync(long userId) => _inner.GetByUserIdAsync(userId);

        public Task<Subscription?> GetByUserIdWithUserAsync(long userId) => _inner.GetByUserIdWithUserAsync(userId);

        public Task<Subscription> CreateAsync(Subscription subscription) => _inner.CreateAsync(subscription);

        public Task<Subscription> UpdateAsync(Subscription subscription)
        {
            throw new InvalidOperationException("Simulated subscription write failure");
        }

        public Task<(List<Subscription> Items, int Total)> GetAllAsync(int limit, int offset, string? statusFilter = null)
            => _inner.GetAllAsync(limit, offset, statusFilter);
    }
}
