using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Models.Billing;

namespace TryOutSpot.Web.Billing;

public static class ComplimentaryPlanGrantMapper
{
    public static bool IsActive(ComplimentaryPlanGrant grant, DateTime now)
    {
        return grant.RevokedAt is null
            && grant.StartsAt <= now
            && (grant.EndsAt is null || grant.EndsAt > now);
    }

    public static string GetStatus(ComplimentaryPlanGrant grant, DateTime now)
    {
        if (grant.RevokedAt is not null)
        {
            return "revoked";
        }

        if (grant.StartsAt > now)
        {
            return "scheduled";
        }

        return grant.EndsAt is not null && grant.EndsAt <= now
            ? "expired"
            : "complimentary";
    }

    public static ComplimentaryPlanGrantResponse ToResponse(ComplimentaryPlanGrant grant, DateTime now)
    {
        var planCode = TryOutSpotBillingCatalog.NormalizePlanCode(grant.PlanType) ?? grant.PlanType;
        var plan = TryOutSpotBillingCatalog.GetPlan(planCode);

        return new ComplimentaryPlanGrantResponse(
            grant.Id,
            grant.UserId,
            planCode,
            plan?.Name ?? grant.PlanType,
            GetStatus(grant, now),
            plan is not null && IsActive(grant, now),
            grant.ScopeType,
            grant.ScopeId,
            grant.StartsAt,
            grant.EndsAt,
            grant.Source,
            grant.PromotionCode,
            grant.Reason,
            grant.GrantedByUserId,
            grant.CreatedAt,
            grant.UpdatedAt,
            grant.RevokedAt,
            grant.RevokedByUserId,
            grant.RevokeReason);
    }
}
