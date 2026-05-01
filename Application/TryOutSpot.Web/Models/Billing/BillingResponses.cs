namespace TryOutSpot.Web.Models.Billing;

/// <summary>
/// Public billing feature returned by the billing catalog API.
/// </summary>
public sealed record BillingFeatureResponse(
    string Code,
    string Name,
    string Description,
    bool IsPaidFeature);

/// <summary>
/// Public billing plan returned by the billing catalog API.
/// </summary>
public sealed record BillingPlanResponse(
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
