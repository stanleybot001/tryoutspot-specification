using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Stripe;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using AppSubscription = TryOutSpot.Web.Data.Entities.Subscription;

namespace TryOutSpot.Web.Services;

public interface IStripeSubscriptionSyncService
{
    Task<AppSubscription?> ApplyStripeSubscriptionAsync(
        StripeSubscriptionSnapshot snapshot,
        CancellationToken cancellationToken);
}

public sealed record StripeSubscriptionSnapshot(
    string StripeSubscriptionId,
    string StripeCustomerId,
    Guid? UserId,
    string? PlanCode,
    string? StripePriceId,
    string Status,
    DateTime? CurrentPeriodStart,
    DateTime? CurrentPeriodEnd,
    DateTime? TrialEnd,
    decimal? Amount,
    string? Currency,
    string? BillingInterval,
    bool CancelAtPeriodEnd,
    DateTime? CancelledAt);

public sealed class StripeSubscriptionSyncService(
    AppDbContext dbContext,
    IOptions<StripeBillingOptions> stripeOptions,
    ILogger<StripeSubscriptionSyncService> logger) : IStripeSubscriptionSyncService
{
    private readonly StripeBillingOptions stripeBillingOptions = stripeOptions.Value;

    public async Task<AppSubscription?> ApplyStripeSubscriptionAsync(
        StripeSubscriptionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(snapshot.StripeSubscriptionId)
            || string.IsNullOrWhiteSpace(snapshot.StripeCustomerId))
        {
            logger.LogWarning("Stripe subscription sync skipped because the subscription or customer id was missing.");
            return null;
        }

        var planCode = ResolvePlanCode(snapshot);
        if (planCode is null)
        {
            logger.LogWarning(
                "Stripe subscription {StripeSubscriptionId} could not be mapped to a TryOutSpot plan.",
                snapshot.StripeSubscriptionId);
            return null;
        }

        var subscription = await FindExistingSubscriptionAsync(snapshot, cancellationToken);
        var now = DateTime.UtcNow;

        if (subscription is null)
        {
            if (snapshot.UserId is null)
            {
                logger.LogWarning(
                    "Stripe subscription {StripeSubscriptionId} could not be linked because no TryOutSpot user id was available.",
                    snapshot.StripeSubscriptionId);
                return null;
            }

            var userExists = await dbContext.Users
                .AnyAsync(user => user.Id == snapshot.UserId.Value && user.IsActive, cancellationToken);
            if (!userExists)
            {
                logger.LogWarning(
                    "Stripe subscription {StripeSubscriptionId} referenced missing or inactive TryOutSpot user {UserId}.",
                    snapshot.StripeSubscriptionId,
                    snapshot.UserId.Value);
                return null;
            }

            subscription = new AppSubscription
            {
                Id = Guid.NewGuid(),
                UserId = snapshot.UserId.Value,
                CreatedAt = now
            };
            dbContext.Subscriptions.Add(subscription);
        }

        subscription.PlanType = planCode;
        subscription.Status = snapshot.Status;
        subscription.StripeCustomerId = snapshot.StripeCustomerId;
        subscription.StripeSubscriptionId = snapshot.StripeSubscriptionId;
        subscription.StripePriceId = snapshot.StripePriceId;
        subscription.CurrentPeriodStart = snapshot.CurrentPeriodStart;
        subscription.CurrentPeriodEnd = snapshot.CurrentPeriodEnd;
        subscription.TrialEnd = snapshot.TrialEnd;
        subscription.Amount = snapshot.Amount;
        subscription.Currency = (snapshot.Currency ?? "USD").ToUpperInvariant();
        subscription.BillingInterval = BillingIntervalCodes.Normalize(snapshot.BillingInterval) ?? BillingIntervalCodes.Month;
        subscription.CancelAtPeriodEnd = snapshot.CancelAtPeriodEnd;
        subscription.CancelledAt = snapshot.CancelledAt;
        subscription.UpdatedAt = now;
        subscription.IsElite = string.Equals(planCode, TryOutSpotPlanCodes.PremiumPlayer, StringComparison.Ordinal);

        await dbContext.SaveChangesAsync(cancellationToken);
        return subscription;
    }

    private async Task<AppSubscription?> FindExistingSubscriptionAsync(
        StripeSubscriptionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var subscription = await dbContext.Subscriptions
            .SingleOrDefaultAsync(
                currentSubscription => currentSubscription.StripeSubscriptionId == snapshot.StripeSubscriptionId,
                cancellationToken);
        if (subscription is not null)
        {
            return subscription;
        }

        if (snapshot.UserId is not null)
        {
            subscription = await dbContext.Subscriptions
                .SingleOrDefaultAsync(
                    currentSubscription => currentSubscription.UserId == snapshot.UserId.Value,
                    cancellationToken);
            if (subscription is not null)
            {
                return subscription;
            }
        }

        return await dbContext.Subscriptions
            .SingleOrDefaultAsync(
                currentSubscription => currentSubscription.StripeCustomerId == snapshot.StripeCustomerId,
                cancellationToken);
    }

    private string? ResolvePlanCode(StripeSubscriptionSnapshot snapshot)
    {
        return TryOutSpotBillingCatalog.NormalizePlanCode(snapshot.PlanCode)
            ?? stripeBillingOptions.FindPlanCodeForPrice(snapshot.StripePriceId);
    }
}

public static class StripeSubscriptionSnapshotFactory
{
    public static StripeSubscriptionSnapshot FromStripeSubscription(Stripe.Subscription subscription)
    {
        var primaryItem = subscription.Items?.Data?.FirstOrDefault();
        var price = primaryItem?.Price;
        var metadata = subscription.Metadata ?? [];
        var planCode = metadata.GetValueOrDefault(StripeBillingMetadataKeys.PlanCode);
        var billingInterval = metadata.GetValueOrDefault(StripeBillingMetadataKeys.BillingInterval)
            ?? price?.Recurring?.Interval;
        var userId = TryParseUserId(metadata.GetValueOrDefault(StripeBillingMetadataKeys.UserId));

        return new StripeSubscriptionSnapshot(
            subscription.Id,
            subscription.CustomerId ?? string.Empty,
            userId,
            planCode,
            price?.Id,
            subscription.Status,
            primaryItem?.CurrentPeriodStart,
            primaryItem?.CurrentPeriodEnd,
            subscription.TrialEnd,
            ToMajorCurrencyAmount(price?.UnitAmountDecimal, price?.UnitAmount),
            price?.Currency ?? subscription.Currency,
            billingInterval,
            subscription.CancelAtPeriodEnd,
            subscription.CanceledAt);
    }

    private static Guid? TryParseUserId(string? rawUserId)
    {
        return Guid.TryParse(rawUserId, out var userId) ? userId : null;
    }

    private static decimal? ToMajorCurrencyAmount(decimal? decimalMinorUnitAmount, long? integerMinorUnitAmount)
    {
        var minorUnitAmount = decimalMinorUnitAmount ?? integerMinorUnitAmount;
        return minorUnitAmount.HasValue ? minorUnitAmount.Value / 100m : null;
    }
}
