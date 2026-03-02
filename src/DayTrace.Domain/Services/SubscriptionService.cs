using DayTrace.Domain.Constants;
using DayTrace.Domain.Entities;
using DayTrace.Domain.Enums;
using DayTrace.Domain.Interfaces;

namespace DayTrace.Domain.Services;

public class SubscriptionService
{
    private readonly ISubscriptionRepository _subscriptionRepo;
    private readonly IStarPaymentRepository _starPaymentRepo;
    private readonly ITransactionExecutor? _transactionExecutor;

    public SubscriptionService(
        ISubscriptionRepository subscriptionRepo,
        IStarPaymentRepository starPaymentRepo,
        ITransactionExecutor? transactionExecutor = null)
    {
        _subscriptionRepo = subscriptionRepo;
        _starPaymentRepo = starPaymentRepo;
        _transactionExecutor = transactionExecutor;
    }

    public async Task<SubscriptionStatusResult> GetStatusAsync(long userId)
    {
        var sub = await _subscriptionRepo.GetByUserIdAsync(userId);

        if (sub == null)
        {
            return new SubscriptionStatusResult(SubscriptionStatus.NotStarted, null, null, null, false);
        }

        if (sub.IsExempt)
        {
            return new SubscriptionStatusResult(SubscriptionStatus.Exempt, sub.TrialExpiresAt, sub.SubscriptionExpiresAt, null, true);
        }

        var now = DateTime.UtcNow;

        if (sub.SubscriptionExpiresAt.HasValue && now < sub.SubscriptionExpiresAt.Value)
        {
            var daysRemaining = (int)(sub.SubscriptionExpiresAt.Value - now).TotalDays;
            return new SubscriptionStatusResult(SubscriptionStatus.Active, sub.TrialExpiresAt, sub.SubscriptionExpiresAt, daysRemaining, false);
        }

        if (sub.TrialExpiresAt.HasValue && now < sub.TrialExpiresAt.Value)
        {
            var daysRemaining = (int)(sub.TrialExpiresAt.Value - now).TotalDays;
            return new SubscriptionStatusResult(SubscriptionStatus.Trial, sub.TrialExpiresAt, sub.SubscriptionExpiresAt, daysRemaining, false);
        }

        if (sub.TrialStartedAt == null && !sub.SubscriptionExpiresAt.HasValue)
        {
            return new SubscriptionStatusResult(SubscriptionStatus.NotStarted, null, null, null, false);
        }

        var lastExpiry = GetLastExpiry(sub);
        var graceEnd = lastExpiry.AddDays(7);
        if (now < graceEnd)
        {
            var daysRemaining = (int)(graceEnd - now).TotalDays;
            return new SubscriptionStatusResult(SubscriptionStatus.GracePeriod, sub.TrialExpiresAt, sub.SubscriptionExpiresAt, daysRemaining, false);
        }

        return new SubscriptionStatusResult(SubscriptionStatus.Expired, sub.TrialExpiresAt, sub.SubscriptionExpiresAt, null, false);
    }

    public async Task<Subscription> StartTrialAsync(long userId, DateOnly firstEventDate)
    {
        var sub = await _subscriptionRepo.GetByUserIdAsync(userId);
        var now = DateTime.UtcNow;

        if (sub == null)
        {
            sub = new Subscription
            {
                UserId = userId,
                TrialStartedAt = now,
                TrialExpiresAt = now.AddDays(30),
                CreatedAt = now,
                UpdatedAt = now
            };
            return await _subscriptionRepo.CreateAsync(sub);
        }

        if (sub.TrialStartedAt != null)
            return sub;

        sub.TrialStartedAt = now;
        sub.TrialExpiresAt = now.AddDays(30);
        sub.UpdatedAt = now;
        return await _subscriptionRepo.UpdateAsync(sub);
    }

    public async Task<bool> ActivateAsync(long userId, string plan, string chargeId)
    {
        if (_transactionExecutor == null)
        {
            return await ActivateCoreAsync(userId, plan, chargeId);
        }

        return await _transactionExecutor.ExecuteAsync(() => ActivateCoreAsync(userId, plan, chargeId));
    }

    private async Task<bool> ActivateCoreAsync(long userId, string plan, string chargeId)
    {
        var existingPayment = await _starPaymentRepo.GetByChargeIdAsync(chargeId);
        if (existingPayment != null)
            return false;

        var starsAmount = plan == "annual" ? SubscriptionPlans.AnnualStars : SubscriptionPlans.MonthlyStars;
        var duration = plan == "annual" ? SubscriptionPlans.AnnualDuration : SubscriptionPlans.MonthlyDuration;
        var now = DateTime.UtcNow;

        var payment = new StarPayment
        {
            UserId = userId,
            Plan = plan,
            StarsAmount = starsAmount,
            TelegramPaymentChargeId = chargeId,
            CreatedAt = now
        };
        await _starPaymentRepo.CreateAsync(payment);

        var sub = await _subscriptionRepo.GetByUserIdAsync(userId);
        if (sub == null)
        {
            sub = new Subscription
            {
                UserId = userId,
                SubscriptionExpiresAt = now + duration,
                CreatedAt = now,
                UpdatedAt = now
            };
            await _subscriptionRepo.CreateAsync(sub);
        }
        else
        {
            var baseDate = sub.SubscriptionExpiresAt.HasValue && sub.SubscriptionExpiresAt.Value > now
                ? sub.SubscriptionExpiresAt.Value
                : now;
            sub.SubscriptionExpiresAt = baseDate + duration;
            sub.UpdatedAt = now;
            await _subscriptionRepo.UpdateAsync(sub);
        }

        return true;
    }

    public async Task ExemptAsync(long userId)
    {
        var now = DateTime.UtcNow;
        var sub = await _subscriptionRepo.GetByUserIdAsync(userId);
        if (sub == null)
        {
            sub = new Subscription
            {
                UserId = userId,
                IsExempt = true,
                CreatedAt = now,
                UpdatedAt = now
            };
            await _subscriptionRepo.CreateAsync(sub);
        }
        else
        {
            sub.IsExempt = true;
            sub.UpdatedAt = now;
            await _subscriptionRepo.UpdateAsync(sub);
        }
    }

    public async Task RemoveExemptAsync(long userId)
    {
        var sub = await _subscriptionRepo.GetByUserIdAsync(userId);
        if (sub == null) return;

        sub.IsExempt = false;
        sub.UpdatedAt = DateTime.UtcNow;
        await _subscriptionRepo.UpdateAsync(sub);
    }

    public async Task ResetTrialAsync(long userId)
    {
        var now = DateTime.UtcNow;
        var sub = await _subscriptionRepo.GetByUserIdAsync(userId);
        if (sub == null)
        {
            sub = new Subscription
            {
                UserId = userId,
                TrialStartedAt = now,
                TrialExpiresAt = now.AddDays(30),
                CreatedAt = now,
                UpdatedAt = now
            };
            await _subscriptionRepo.CreateAsync(sub);
        }
        else
        {
            sub.TrialStartedAt = now;
            sub.TrialExpiresAt = now.AddDays(30);
            sub.UpdatedAt = now;
            await _subscriptionRepo.UpdateAsync(sub);
        }
    }

    private static DateTime GetLastExpiry(Subscription sub)
    {
        var trialExpiry = sub.TrialExpiresAt ?? DateTime.MinValue;
        var subExpiry = sub.SubscriptionExpiresAt ?? DateTime.MinValue;
        return trialExpiry > subExpiry ? trialExpiry : subExpiry;
    }
}
