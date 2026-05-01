namespace TryOutSpot.Web.Billing;

public sealed record BillingPlanDefinition(
    string Code,
    string Name,
    string Audience,
    string Description,
    decimal MonthlyAmount,
    decimal? AnnualAmount,
    string Currency,
    int? TrialDays,
    bool RequiresStripeSubscription,
    IReadOnlyCollection<string> IncludedFeatureCodes);
