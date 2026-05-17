using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;

namespace TryOutSpot.Web.Services;

public sealed class EntitlementService(
    AppDbContext dbContext,
    UserManager<User> userManager) : IEntitlementService
{
    public async Task<UserEntitlementSet?> GetEntitlementsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users
            .Include(currentUser => currentUser.Subscriptions)
            .SingleOrDefaultAsync(currentUser => currentUser.Id == userId, cancellationToken);

        if (user is not { IsActive: true })
        {
            return null;
        }

        var accountTypes = TryOutSpotRoles.CanonicalizeRoleSet(await userManager.GetRolesAsync(user));
        var features = new SortedSet<string>(
            TryOutSpotBillingCatalog.GetFreeFeatureCodesForRoles(accountTypes),
            StringComparer.Ordinal);

        var activePlanCodes = new SortedSet<string>(StringComparer.Ordinal);
        string? primaryPlanCode = null;
        string? primarySubscriptionStatus = null;
        foreach (var subscription in user.Subscriptions
            .Where(subscription => TryOutSpotBillingCatalog.IsEntitlingSubscriptionStatus(subscription.Status))
            .OrderByDescending(subscription => subscription.UpdatedAt))
        {
            var planCode = ResolvePlanCode(subscription);
            if (planCode is null || TryOutSpotBillingCatalog.GetPlan(planCode) is not { } plan)
            {
                continue;
            }

            features.UnionWith(plan.IncludedFeatureCodes);
            activePlanCodes.Add(planCode);
            primaryPlanCode ??= planCode;
            primarySubscriptionStatus ??= subscription.Status;
        }

        var hasPaidTeamPlan = activePlanCodes.Contains(TryOutSpotPlanCodes.TeamBasic)
            || activePlanCodes.Contains(TryOutSpotPlanCodes.TeamProfessional)
            || activePlanCodes.Contains(TryOutSpotPlanCodes.EnterpriseOrganization);
        var qualifiesForFreeCoach = accountTypes.Any(TryOutSpotRoles.IsTeamBundleRole);

        if (qualifiesForFreeCoach && !hasPaidTeamPlan)
        {
            activePlanCodes.Add(TryOutSpotPlanCodes.FreeCoach);
            primaryPlanCode ??= TryOutSpotPlanCodes.FreeCoach;
            primarySubscriptionStatus ??= "active";
        }

        return new UserEntitlementSet(
            user.Id,
            primaryPlanCode,
            primarySubscriptionStatus,
            accountTypes.ToArray(),
            features.ToArray())
        {
            ActivePlanCodes = activePlanCodes.ToArray()
        };
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
