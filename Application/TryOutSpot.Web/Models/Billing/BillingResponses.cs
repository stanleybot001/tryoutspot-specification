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
    bool CancelAtPeriodEnd)
{
    public IReadOnlyCollection<CurrentBillingSubscriptionResponse> Subscriptions { get; init; } = [];

    public IReadOnlyCollection<ComplimentaryPlanGrantResponse> ComplimentaryGrants { get; init; } = [];
}

/// <summary>
/// A single subscription currently linked to the authenticated user.
/// </summary>
public sealed record CurrentBillingSubscriptionResponse(
    Guid Id,
    string PlanCode,
    string PlanName,
    string Status,
    bool HasActiveEntitlement,
    string BillingInterval,
    decimal? Amount,
    string Currency,
    DateTime? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    string ScopeType,
    Guid? ScopeId);

/// <summary>
/// Complimentary plan access granted by an admin or promotion.
/// </summary>
public sealed record ComplimentaryPlanGrantResponse(
    Guid Id,
    Guid UserId,
    string PlanCode,
    string PlanName,
    string Status,
    bool HasActiveEntitlement,
    string ScopeType,
    Guid? ScopeId,
    DateTime StartsAt,
    DateTime? EndsAt,
    string Source,
    string? PromotionCode,
    string? Reason,
    Guid GrantedByUserId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? RevokedAt,
    Guid? RevokedByUserId,
    string? RevokeReason);

/// <summary>
/// Request for a platform admin to grant complimentary plan access.
/// </summary>
public sealed record CreateComplimentaryPlanGrantRequest(
    Guid UserId,
    string PlanCode,
    int? DurationMonths = null,
    DateTime? StartsAt = null,
    DateTime? EndsAt = null,
    string? ScopeType = null,
    Guid? ScopeId = null,
    string? Reason = null);

/// <summary>
/// Request for a platform admin to revoke complimentary plan access.
/// </summary>
public sealed record RevokeComplimentaryPlanGrantRequest(
    string? Reason = null);

/// <summary>
/// Response returned after claiming a promotion.
/// </summary>
public sealed record PromotionClaimResponse(
    string PromotionCode,
    bool Claimed,
    DateTime? EndsAt,
    IReadOnlyCollection<ComplimentaryPlanGrantResponse> Grants);

/// <summary>
/// Platform admin status summary for the launch founder promotion.
/// </summary>
public sealed record LaunchPromotionStatusResponse(
    string PromotionCode,
    string PromotionName,
    int ClaimedCount,
    int RemainingCount,
    int MaxClaims,
    int GrantMonths,
    int ActiveGrantCount,
    DateTime? LatestGrantEndsAt,
    int Limit,
    int Offset,
    IReadOnlyCollection<LaunchPromotionClaimResponse> RecentClaims);

/// <summary>
/// Request for platform admins to configure or reset the launch founder promotion.
/// </summary>
public sealed record LaunchPromotionSettingsRequest(
    string? Name,
    int MaxRedemptions,
    int GrantMonths);

/// <summary>
/// Platform admin claim summary for a launch founder promotion redemption.
/// </summary>
public sealed record LaunchPromotionClaimResponse(
    Guid UserId,
    string UserDisplayName,
    string UserEmail,
    IReadOnlyCollection<string> GrantedPlanCodes,
    int ActiveGrantCount,
    DateTime? LatestGrantEndsAt,
    DateTime RedeemedAt);

/// <summary>
/// Request to create a Stripe Checkout subscription session.
/// </summary>
public sealed record CreateCheckoutSessionRequest(
    string PlanCode,
    string? BillingInterval,
    string? ScopeType = null,
    Guid? ScopeId = null);

/// <summary>
/// Stripe Checkout session details returned to the frontend.
/// </summary>
public sealed record CheckoutSessionResponse(
    string SessionId,
    string Url,
    string PlanCode,
    string BillingInterval)
{
    public Guid? SubscriptionId { get; init; }

    public string ScopeType { get; init; } = "account";

    public Guid? ScopeId { get; init; }
}

/// <summary>
/// Stripe customer portal session details returned to the frontend.
/// </summary>
public sealed record BillingPortalSessionResponse(
    string Url);
