namespace TryOutSpot.Web.Billing;

public sealed record UserEntitlementSet(
    Guid UserId,
    string? PlanCode,
    string? SubscriptionStatus,
    IReadOnlyCollection<string> AccountTypes,
    IReadOnlyCollection<string> FeatureCodes)
{
    public IReadOnlyCollection<string> ActivePlanCodes { get; init; } = [];
}
