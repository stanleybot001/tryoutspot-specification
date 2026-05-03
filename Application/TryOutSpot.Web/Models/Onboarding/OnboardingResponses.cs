using TryOutSpot.Web.Models.Account;
using TryOutSpot.Web.Models.Billing;

namespace TryOutSpot.Web.Models.Onboarding;

/// <summary>
/// Public onboarding options used by signup and first-run UI.
/// </summary>
public sealed record OnboardingOptionsResponse(
    IReadOnlyCollection<OnboardingAccountTypeOptionResponse> AccountTypes,
    IReadOnlyCollection<BillingPlanResponse> Plans,
    bool CanSkipPlanSelection,
    string RecommendedFlow);

/// <summary>
/// Public account type option with frontend-friendly labels.
/// </summary>
public sealed record OnboardingAccountTypeOptionResponse(
    string Name,
    string Label,
    string Description,
    bool IsCommonFirstChoice);

/// <summary>
/// Signed-in user's current onboarding state.
/// </summary>
public sealed record OnboardingStatusResponse(
    CurrentUserResponse User,
    IReadOnlyCollection<OnboardingStepResponse> Steps,
    IReadOnlyCollection<string> FeatureCodes,
    IReadOnlyCollection<BillingPlanResponse> RecommendedPlans,
    bool CanSkipPlanSelection,
    bool IsComplete);

/// <summary>
/// A single required or optional onboarding step.
/// </summary>
public sealed record OnboardingStepResponse(
    string Code,
    string Title,
    string Description,
    bool IsRequired,
    bool IsComplete,
    string? ActionUrl);
