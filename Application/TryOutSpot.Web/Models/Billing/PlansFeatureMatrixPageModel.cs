namespace TryOutSpot.Web.Models.Billing;

public sealed class PlansFeatureMatrixPageModel
{
    public required IReadOnlyCollection<AccountTypePlanAccessRow> AccountTypePlanAccess { get; init; }

    public required IReadOnlyCollection<FeatureBundlePageItem> FeatureBundles { get; init; }

    public required IReadOnlyCollection<PlanTrackPageItem> PlanTracks { get; init; }

    public required IReadOnlyCollection<string> CriticalPolicyNotes { get; init; }
}

public sealed record AccountTypePlanAccessRow(
    string AccountType,
    bool FreePlayerParent,
    bool PremiumPlayer,
    bool TeamBasic,
    bool TeamOffseasonHold,
    bool TeamProfessional,
    bool EnterpriseOrganization);

public sealed record FeatureBundlePageItem(
    string Name,
    string Scope,
    string ComparedTo,
    IReadOnlyCollection<FeatureDetailPageItem> Features);

public sealed record PlanTrackPageItem(
    string TrackName,
    IReadOnlyCollection<PlanDetailPageItem> Plans);

public sealed record PlanDetailPageItem(
    string Code,
    string Name,
    string Audience,
    string Description,
    string PriceLabel,
    int? TrialDays,
    IReadOnlyCollection<FeatureDetailPageItem> IncludedFeatures,
    IReadOnlyCollection<FeatureDetailPageItem> AddedFeaturesComparedToPrevious);

public sealed record FeatureDetailPageItem(
    string Code,
    string Name,
    string Description);
