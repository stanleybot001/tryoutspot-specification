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
    IReadOnlyCollection<string> IncludedFeatureCodes)
{
    public IReadOnlyCollection<BillingPlanPriceResponse> Prices { get; init; } = [];
}

/// <summary>
/// Public price option for a billing plan.
/// </summary>
public sealed record BillingPlanPriceResponse(
    string BillingInterval,
    decimal Amount,
    string Currency,
    bool IsCheckoutConfigured);

/// <summary>
/// Current authenticated user's billing status.
/// </summary>
public sealed record CurrentBillingResponse(
    string? PlanCode,
    string? PlanName,
    string? Status,
    bool HasActiveEntitlement,
    string? BillingInterval,
    decimal? Amount,
    string? Currency,
    DateTime? CurrentPeriodEnd,
    bool CancelAtPeriodEnd);

/// <summary>
/// Request to create a Stripe Checkout subscription session.
/// </summary>
public sealed record CreateCheckoutSessionRequest(
    string PlanCode,
    string? BillingInterval);

/// <summary>
/// Stripe Checkout session details returned to the frontend.
/// </summary>
public sealed record CheckoutSessionResponse(
    string SessionId,
    string Url,
    string PlanCode,
    string BillingInterval);

/// <summary>
/// Stripe customer portal session details returned to the frontend.
/// </summary>
public sealed record BillingPortalSessionResponse(
    string Url);
