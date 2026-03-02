using DayTrace.Domain.Entities;
using DayTrace.Domain.Enums;
using DayTrace.Domain.Interfaces;
using DayTrace.Domain.Services;
using Moq;

namespace DayTrace.Tests.Services;

/// <summary>
/// Unit tests for SubscriptionService business logic.
/// Tests use Moq for repository mocking.
/// </summary>
public class SubscriptionServiceTests
{
    private readonly Mock<ISubscriptionRepository> _subscriptionRepoMock;
    private readonly Mock<IStarPaymentRepository> _starPaymentRepoMock;
    private readonly SubscriptionService _service;

    public SubscriptionServiceTests()
    {
        _subscriptionRepoMock = new Mock<ISubscriptionRepository>();
        _starPaymentRepoMock = new Mock<IStarPaymentRepository>();
        _service = new SubscriptionService(_subscriptionRepoMock.Object, _starPaymentRepoMock.Object);
    }

    // ========== GetStatusAsync ==========

    [Fact]
    public async Task GetStatus_NoSubscription_ReturnsNotStarted()
    {
        _subscriptionRepoMock
            .Setup(r => r.GetByUserIdAsync(1))
            .ReturnsAsync((Subscription?)null);

        var result = await _service.GetStatusAsync(1);

        Assert.Equal(SubscriptionStatus.NotStarted, result.Status);
        Assert.True(result.HasAccess);
        Assert.False(result.IsExempt);
    }

    [Fact]
    public async Task GetStatus_SubscriptionExists_TrialNotStarted_ReturnsNotStarted()
    {
        _subscriptionRepoMock
            .Setup(r => r.GetByUserIdAsync(1))
            .ReturnsAsync(new Subscription { UserId = 1, TrialStartedAt = null });

        var result = await _service.GetStatusAsync(1);

        Assert.Equal(SubscriptionStatus.NotStarted, result.Status);
        Assert.True(result.HasAccess);
    }

    [Fact]
    public async Task GetStatus_TrialActive_ReturnsTrial()
    {
        var trialExpires = DateTime.UtcNow.AddDays(15);
        _subscriptionRepoMock
            .Setup(r => r.GetByUserIdAsync(1))
            .ReturnsAsync(new Subscription
            {
                UserId = 1,
                TrialStartedAt = DateTime.UtcNow.AddDays(-15),
                TrialExpiresAt = trialExpires
            });

        var result = await _service.GetStatusAsync(1);

        Assert.Equal(SubscriptionStatus.Trial, result.Status);
        Assert.True(result.HasAccess);
        Assert.Equal(trialExpires, result.TrialExpiresAt);
        Assert.NotNull(result.DaysRemaining);
        Assert.True(result.DaysRemaining > 0);
    }

    [Fact]
    public async Task GetStatus_SubscriptionActive_ReturnsActive()
    {
        var subExpires = DateTime.UtcNow.AddDays(20);
        _subscriptionRepoMock
            .Setup(r => r.GetByUserIdAsync(1))
            .ReturnsAsync(new Subscription
            {
                UserId = 1,
                TrialStartedAt = DateTime.UtcNow.AddDays(-45),
                TrialExpiresAt = DateTime.UtcNow.AddDays(-15), // trial expired
                SubscriptionExpiresAt = subExpires
            });

        var result = await _service.GetStatusAsync(1);

        Assert.Equal(SubscriptionStatus.Active, result.Status);
        Assert.True(result.HasAccess);
        Assert.Equal(subExpires, result.SubscriptionExpiresAt);
    }

    [Fact]
    public async Task GetStatus_TrialExpiredWithinGrace_ReturnsGracePeriod()
    {
        // Trial expired 3 days ago, grace period is 7 days → still in grace
        _subscriptionRepoMock
            .Setup(r => r.GetByUserIdAsync(1))
            .ReturnsAsync(new Subscription
            {
                UserId = 1,
                TrialStartedAt = DateTime.UtcNow.AddDays(-33),
                TrialExpiresAt = DateTime.UtcNow.AddDays(-3)
            });

        var result = await _service.GetStatusAsync(1);

        Assert.Equal(SubscriptionStatus.GracePeriod, result.Status);
        Assert.True(result.HasAccess);
        Assert.NotNull(result.DaysRemaining);
    }

    [Fact]
    public async Task GetStatus_TrialExpiredAfterGrace_ReturnsExpired()
    {
        // Trial expired 10 days ago, grace is 7 days → blocked
        _subscriptionRepoMock
            .Setup(r => r.GetByUserIdAsync(1))
            .ReturnsAsync(new Subscription
            {
                UserId = 1,
                TrialStartedAt = DateTime.UtcNow.AddDays(-40),
                TrialExpiresAt = DateTime.UtcNow.AddDays(-10)
            });

        var result = await _service.GetStatusAsync(1);

        Assert.Equal(SubscriptionStatus.Expired, result.Status);
        Assert.False(result.HasAccess);
        Assert.Null(result.DaysRemaining);
    }

    [Fact]
    public async Task GetStatus_IsExempt_ReturnsExempt()
    {
        _subscriptionRepoMock
            .Setup(r => r.GetByUserIdAsync(1))
            .ReturnsAsync(new Subscription
            {
                UserId = 1,
                IsExempt = true,
                TrialExpiresAt = DateTime.UtcNow.AddDays(-10) // expired, but exempt
            });

        var result = await _service.GetStatusAsync(1);

        Assert.Equal(SubscriptionStatus.Exempt, result.Status);
        Assert.True(result.HasAccess);
        Assert.True(result.IsExempt);
    }

    // ========== StartTrialAsync ==========

    [Fact]
    public async Task StartTrial_NoSubscriptionExists_CreatesNewWithTrial()
    {
        _subscriptionRepoMock
            .Setup(r => r.GetByUserIdAsync(1))
            .ReturnsAsync((Subscription?)null);

        _subscriptionRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<Subscription>()))
            .ReturnsAsync((Subscription s) => s);

        var result = await _service.StartTrialAsync(1, DateOnly.FromDateTime(DateTime.UtcNow));

        Assert.NotNull(result.TrialStartedAt);
        Assert.NotNull(result.TrialExpiresAt);
        Assert.True(result.TrialExpiresAt > DateTime.UtcNow.AddDays(28));
        _subscriptionRepoMock.Verify(r => r.CreateAsync(It.IsAny<Subscription>()), Times.Once);
    }

    [Fact]
    public async Task StartTrial_TrialAlreadyStarted_DoesNotUpdate()
    {
        var existingSub = new Subscription
        {
            UserId = 1,
            TrialStartedAt = DateTime.UtcNow.AddDays(-5),
            TrialExpiresAt = DateTime.UtcNow.AddDays(25)
        };
        _subscriptionRepoMock
            .Setup(r => r.GetByUserIdAsync(1))
            .ReturnsAsync(existingSub);

        var result = await _service.StartTrialAsync(1, DateOnly.FromDateTime(DateTime.UtcNow));

        _subscriptionRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Subscription>()), Times.Never);
        _subscriptionRepoMock.Verify(r => r.CreateAsync(It.IsAny<Subscription>()), Times.Never);
    }

    // ========== ActivateAsync ==========

    [Fact]
    public async Task Activate_NewPayment_CreatesPaymentAndUpdatesSubscription()
    {
        _starPaymentRepoMock
            .Setup(r => r.GetByChargeIdAsync("charge_123"))
            .ReturnsAsync((StarPayment?)null);

        _starPaymentRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<StarPayment>()))
            .ReturnsAsync((StarPayment p) => p);

        _subscriptionRepoMock
            .Setup(r => r.GetByUserIdAsync(1))
            .ReturnsAsync((Subscription?)null);

        _subscriptionRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<Subscription>()))
            .ReturnsAsync((Subscription s) => s);

        var activated = await _service.ActivateAsync(1, "monthly", "charge_123");

        Assert.True(activated);
        _starPaymentRepoMock.Verify(r => r.CreateAsync(It.Is<StarPayment>(p =>
            p.Plan == "monthly" && p.StarsAmount == 100 && p.TelegramPaymentChargeId == "charge_123"
        )), Times.Once);
    }

    [Fact]
    public async Task Activate_DuplicateChargeId_ReturnsFalseAndSkips()
    {
        _starPaymentRepoMock
            .Setup(r => r.GetByChargeIdAsync("charge_dup"))
            .ReturnsAsync(new StarPayment { TelegramPaymentChargeId = "charge_dup" });

        var activated = await _service.ActivateAsync(1, "monthly", "charge_dup");

        Assert.False(activated);
        _starPaymentRepoMock.Verify(r => r.CreateAsync(It.IsAny<StarPayment>()), Times.Never);
        _subscriptionRepoMock.Verify(r => r.UpdateAsync(It.IsAny<Subscription>()), Times.Never);
    }

    [Fact]
    public async Task Activate_AnnualPlan_Uses960StarsAnd365Days()
    {
        _starPaymentRepoMock
            .Setup(r => r.GetByChargeIdAsync(It.IsAny<string>()))
            .ReturnsAsync((StarPayment?)null);

        _starPaymentRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<StarPayment>()))
            .ReturnsAsync((StarPayment p) => p);

        _subscriptionRepoMock
            .Setup(r => r.GetByUserIdAsync(1))
            .ReturnsAsync((Subscription?)null);

        _subscriptionRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<Subscription>()))
            .ReturnsAsync((Subscription s) => s);

        await _service.ActivateAsync(1, "annual", "charge_annual");

        _starPaymentRepoMock.Verify(r => r.CreateAsync(It.Is<StarPayment>(p =>
            p.StarsAmount == 960 && p.Plan == "annual"
        )), Times.Once);

        _subscriptionRepoMock.Verify(r => r.CreateAsync(It.Is<Subscription>(s =>
            s.SubscriptionExpiresAt >= DateTime.UtcNow.AddDays(364)
        )), Times.Once);
    }

    [Fact]
    public async Task Activate_ExistingActiveSubscription_ExtendsFromExpiryDate()
    {
        var existingExpiry = DateTime.UtcNow.AddDays(15); // still active
        _starPaymentRepoMock
            .Setup(r => r.GetByChargeIdAsync(It.IsAny<string>()))
            .ReturnsAsync((StarPayment?)null);

        _starPaymentRepoMock
            .Setup(r => r.CreateAsync(It.IsAny<StarPayment>()))
            .ReturnsAsync((StarPayment p) => p);

        _subscriptionRepoMock
            .Setup(r => r.GetByUserIdAsync(1))
            .ReturnsAsync(new Subscription { UserId = 1, SubscriptionExpiresAt = existingExpiry });

        _subscriptionRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Subscription>()))
            .ReturnsAsync((Subscription s) => s);

        await _service.ActivateAsync(1, "monthly", "charge_extend");

        _subscriptionRepoMock.Verify(r => r.UpdateAsync(It.Is<Subscription>(s =>
            s.SubscriptionExpiresAt >= existingExpiry.AddDays(28) // extended from existing expiry
        )), Times.Once);
    }

    [Fact]
    public async Task Activate_WithTransactionExecutor_RunsInsideTransactionBoundary()
    {
        var transactionExecutorMock = new Mock<ITransactionExecutor>();
        transactionExecutorMock
            .Setup(t => t.ExecuteAsync(It.IsAny<Func<Task<bool>>>()))
            .Returns<Func<Task<bool>>>(operation => operation());

        _starPaymentRepoMock
            .Setup(r => r.GetByChargeIdAsync("charge_dup"))
            .ReturnsAsync(new StarPayment { TelegramPaymentChargeId = "charge_dup" });

        var service = new SubscriptionService(
            _subscriptionRepoMock.Object,
            _starPaymentRepoMock.Object,
            transactionExecutorMock.Object);

        var activated = await service.ActivateAsync(1, "monthly", "charge_dup");

        Assert.False(activated);
        transactionExecutorMock.Verify(t => t.ExecuteAsync(It.IsAny<Func<Task<bool>>>()), Times.Once);
    }

    // ========== ExemptAsync / RemoveExemptAsync ==========

    [Fact]
    public async Task Exempt_SetsIsExemptTrue()
    {
        var sub = new Subscription { UserId = 1, IsExempt = false };
        _subscriptionRepoMock
            .Setup(r => r.GetByUserIdAsync(1))
            .ReturnsAsync(sub);
        _subscriptionRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Subscription>()))
            .ReturnsAsync((Subscription s) => s);

        await _service.ExemptAsync(1);

        _subscriptionRepoMock.Verify(r => r.UpdateAsync(It.Is<Subscription>(s => s.IsExempt)), Times.Once);
    }

    [Fact]
    public async Task RemoveExempt_SetsIsExemptFalse()
    {
        var sub = new Subscription { UserId = 1, IsExempt = true };
        _subscriptionRepoMock
            .Setup(r => r.GetByUserIdAsync(1))
            .ReturnsAsync(sub);
        _subscriptionRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Subscription>()))
            .ReturnsAsync((Subscription s) => s);

        await _service.RemoveExemptAsync(1);

        _subscriptionRepoMock.Verify(r => r.UpdateAsync(It.Is<Subscription>(s => !s.IsExempt)), Times.Once);
    }

    // ========== ResetTrialAsync ==========

    [Fact]
    public async Task ResetTrial_ExistingSubscription_ResetsTrialDates()
    {
        var oldExpiry = DateTime.UtcNow.AddDays(-10);
        var sub = new Subscription
        {
            UserId = 1,
            TrialStartedAt = DateTime.UtcNow.AddDays(-40),
            TrialExpiresAt = oldExpiry
        };
        _subscriptionRepoMock
            .Setup(r => r.GetByUserIdAsync(1))
            .ReturnsAsync(sub);
        _subscriptionRepoMock
            .Setup(r => r.UpdateAsync(It.IsAny<Subscription>()))
            .ReturnsAsync((Subscription s) => s);

        await _service.ResetTrialAsync(1);

        _subscriptionRepoMock.Verify(r => r.UpdateAsync(It.Is<Subscription>(s =>
            s.TrialExpiresAt > oldExpiry // new expiry is in the future
        )), Times.Once);
    }

    // ========== HasAccess boundary tests ==========

    [Theory]
    [InlineData(SubscriptionStatus.NotStarted, true)]
    [InlineData(SubscriptionStatus.Trial, true)]
    [InlineData(SubscriptionStatus.Active, true)]
    [InlineData(SubscriptionStatus.GracePeriod, true)]
    [InlineData(SubscriptionStatus.Exempt, true)]
    [InlineData(SubscriptionStatus.Expired, false)]
    public void SubscriptionStatusResult_HasAccess_CorrectForAllStatuses(SubscriptionStatus status, bool expectedAccess)
    {
        var result = new SubscriptionStatusResult(status, null, null, null, false);
        Assert.Equal(expectedAccess, result.HasAccess);
    }
}
