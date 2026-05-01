using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;

namespace TryOutSpot.Web.Services;

public sealed class EntitlementService(
    AppDbContext dbContext,
    UserManager<User> userManager) : IEntitlementService
{
    public async Task<UserEntitlementSet?> GetEntitlementsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .Include(currentUser => currentUser.Subscription)
            .SingleOrDefaultAsync(currentUser => currentUser.Id == userId, cancellationToken);

        if (user is not { IsActive: true })
        {
            return null;
        }

        var accountTypes = await userManager.GetRolesAsync(user);
        var features = new SortedSet<string>(
            TryOutSpotBillingCatalog.GetFreeFeatureCodesForRoles(accountTypes),
            StringComparer.Ordinal);

        var planCode = ResolvePlanCode(user.Subscription);
        if (planCode is not null
            && TryOutSpotBillingCatalog.IsEntitlingSubscriptionStatus(user.Subscription?.Status)
            && TryOutSpotBillingCatalog.GetPlan(planCode) is { } plan)
        {
            features.UnionWith(plan.IncludedFeatureCodes);
        }

        return new UserEntitlementSet(
            user.Id,
            planCode,
            user.Subscription?.Status,
            accountTypes.ToArray(),
            features.ToArray());
    }

    public async Task<bool> HasFeatureAsync(Guid userId, string featureCode, CancellationToken cancellationToken)
    {
        var entitlements = await GetEntitlementsAsync(userId, cancellationToken);
        return entitlements?.FeatureCodes.Contains(featureCode, StringComparer.Ordinal) == true;
    }

    private static string? ResolvePlanCode(Subscription? subscription)
    {
        if (subscription is null)
        {
            return null;
        }

        return TryOutSpotBillingCatalog.NormalizePlanCode(subscription.PlanType)
            ?? (subscription.IsElite ? TryOutSpotPlanCodes.PremiumPlayer : null);
    }
}
