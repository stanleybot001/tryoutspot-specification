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
    string? ScopeType,
    Guid? ScopeId,
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

        var scopeType = TryOutSpotSubscriptionScopeTypes.Normalize(snapshot.ScopeType)
            ?? TryOutSpotSubscriptionScopeTypes.Account;
        var subscription = await FindExistingSubscriptionAsync(snapshot, planCode, scopeType, cancellationToken);
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
        subscription.ScopeType = scopeType;
        subscription.ScopeId = snapshot.ScopeId;
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

        var hasEntitlement = TryOutSpotBillingCatalog.IsEntitlingSubscriptionStatus(snapshot.Status);
        await ApplyPlanLifecycleRulesAsync(
            subscription.UserId,
            planCode,
            hasEntitlement,
            snapshot.CancelAtPeriodEnd,
            now,
            cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        return subscription;
    }

    private async Task<AppSubscription?> FindExistingSubscriptionAsync(
        StripeSubscriptionSnapshot snapshot,
        string planCode,
        string scopeType,
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
                .OrderByDescending(currentSubscription => currentSubscription.CreatedAt)
                .FirstOrDefaultAsync(
                    currentSubscription => currentSubscription.UserId == snapshot.UserId.Value
                        && currentSubscription.PlanType == planCode
                        && currentSubscription.ScopeType == scopeType
                        && currentSubscription.ScopeId == snapshot.ScopeId,
                    cancellationToken);
            if (subscription is not null)
            {
                return subscription;
            }
        }

        return null;
    }

    private async Task ApplyPlanLifecycleRulesAsync(
        Guid userId,
        string planCode,
        bool hasEntitlement,
        bool cancelAtPeriodEnd,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var normalizedPlanCode = TryOutSpotBillingCatalog.NormalizePlanCode(planCode);
        if (normalizedPlanCode is null)
        {
            return;
        }

        if (normalizedPlanCode is TryOutSpotPlanCodes.TeamOffseasonHold)
        {
            if (hasEntitlement)
            {
                await ApplyTeamDirectoryHoldAsync(userId, now, cancellationToken);
            }

            return;
        }

        if (normalizedPlanCode is TryOutSpotPlanCodes.TeamBasic && hasEntitlement)
        {
            await RestoreTeamDirectoryVisibilityAsync(userId, now, cancellationToken);
            return;
        }

        if (normalizedPlanCode is TryOutSpotPlanCodes.TeamProfessional or TryOutSpotPlanCodes.EnterpriseOrganization)
        {
            if (!hasEntitlement || cancelAtPeriodEnd)
            {
                await SoftDeactivateTeamAndOrganizationDataAsync(userId, now, cancellationToken);
                return;
            }

            await RestoreTeamDirectoryVisibilityAsync(userId, now, cancellationToken);
        }
    }

    private async Task ApplyTeamDirectoryHoldAsync(
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var teams = await dbContext.Teams
            .Where(team => team.IsActive
                && team.UserTeamRoles.Any(role => role.UserId == userId))
            .ToArrayAsync(cancellationToken);

        foreach (var team in teams)
        {
            team.IsSearchable = true;
            team.IsContactInfoVisible = false;
            team.UpdatedAt = now;
        }

        var organizationIds = teams
            .Where(team => team.OrganizationId is not null)
            .Select(team => team.OrganizationId!.Value)
            .Distinct()
            .ToArray();
        if (organizationIds.Length == 0)
        {
            return;
        }

        var organizations = await dbContext.Organizations
            .Where(organization => organization.IsActive && organizationIds.Contains(organization.Id))
            .ToArrayAsync(cancellationToken);
        foreach (var organization in organizations)
        {
            organization.IsSearchable = true;
            organization.IsContactInfoVisible = false;
            organization.UpdatedAt = now;
        }
    }

    private async Task RestoreTeamDirectoryVisibilityAsync(
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var teams = await dbContext.Teams
            .Where(team => team.IsActive
                && team.UserTeamRoles.Any(role => role.UserId == userId))
            .ToArrayAsync(cancellationToken);

        foreach (var team in teams)
        {
            team.IsContactInfoVisible = true;
            team.UpdatedAt = now;
        }

        var organizationIds = teams
            .Where(team => team.OrganizationId is not null)
            .Select(team => team.OrganizationId!.Value)
            .Distinct()
            .ToArray();
        if (organizationIds.Length == 0)
        {
            return;
        }

        var organizations = await dbContext.Organizations
            .Where(organization => organization.IsActive && organizationIds.Contains(organization.Id))
            .ToArrayAsync(cancellationToken);
        foreach (var organization in organizations)
        {
            organization.IsContactInfoVisible = true;
            organization.UpdatedAt = now;
        }
    }

    private async Task SoftDeactivateTeamAndOrganizationDataAsync(
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var teams = await dbContext.Teams
            .Where(team => team.UserTeamRoles.Any(role => role.UserId == userId))
            .ToArrayAsync(cancellationToken);
        if (teams.Length == 0)
        {
            return;
        }

        var teamIds = teams.Select(team => team.Id).ToArray();
        foreach (var team in teams)
        {
            team.IsActive = false;
            team.IsSearchable = false;
            team.IsContactInfoVisible = false;
            team.WebsiteUrl = null;
            team.PhoneNumber = null;
            team.Email = null;
            team.SocialMediaLinks = null;
            team.UpdatedAt = now;
        }

        var teamRoles = await dbContext.UserTeamRoles
            .Where(role => role.IsActive
                && role.UserId == userId
                && teamIds.Contains(role.TeamId))
            .ToArrayAsync(cancellationToken);
        foreach (var teamRole in teamRoles)
        {
            teamRole.IsActive = false;
            teamRole.EndDate ??= now;
        }

        var teamSports = await dbContext.TeamSports
            .Where(teamSport => teamSport.IsActive && teamIds.Contains(teamSport.TeamId))
            .ToArrayAsync(cancellationToken);
        foreach (var teamSport in teamSports)
        {
            teamSport.IsActive = false;
        }

        var opportunities = await dbContext.Opportunities
            .Where(opportunity => teamIds.Contains(opportunity.TeamId)
                && (opportunity.IsActive || opportunity.IsPublished))
            .ToArrayAsync(cancellationToken);
        foreach (var opportunity in opportunities)
        {
            opportunity.IsActive = false;
            opportunity.IsPublished = false;
            opportunity.ExpiresAt ??= now;
            opportunity.ContactEmail = null;
            opportunity.ContactPhone = null;
            opportunity.WebsiteUrl = null;
            opportunity.PdfUrl = null;
            opportunity.UpdatedAt = now;
        }

        var organizationIds = teams
            .Where(team => team.OrganizationId is not null)
            .Select(team => team.OrganizationId!.Value)
            .Distinct()
            .ToArray();
        if (organizationIds.Length == 0)
        {
            return;
        }

        var organizations = await dbContext.Organizations
            .Where(organization => organizationIds.Contains(organization.Id))
            .ToArrayAsync(cancellationToken);
        foreach (var organization in organizations)
        {
            organization.IsActive = false;
            organization.IsSearchable = false;
            organization.IsContactInfoVisible = false;
            organization.WebsiteUrl = null;
            organization.PhoneNumber = null;
            organization.Email = null;
            organization.SocialMediaLinks = null;
            organization.UpdatedAt = now;
        }
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
        var scopeType = metadata.GetValueOrDefault(StripeBillingMetadataKeys.ScopeType);
        var scopeId = TryParseGuid(metadata.GetValueOrDefault(StripeBillingMetadataKeys.ScopeId));

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
            scopeType,
            scopeId,
            subscription.CancelAtPeriodEnd,
            subscription.CanceledAt);
    }

    private static Guid? TryParseUserId(string? rawUserId)
    {
        return TryParseGuid(rawUserId);
    }

    private static Guid? TryParseGuid(string? rawValue)
    {
        return Guid.TryParse(rawValue, out var value) ? value : null;
    }

    private static decimal? ToMajorCurrencyAmount(decimal? decimalMinorUnitAmount, long? integerMinorUnitAmount)
    {
        var minorUnitAmount = decimalMinorUnitAmount ?? integerMinorUnitAmount;
        return minorUnitAmount.HasValue ? minorUnitAmount.Value / 100m : null;
    }
}
