namespace TryOutSpot.Web.Models.Dashboard;

public static class DashboardActivityTypeCodes
{
    public const string Tryouts = "tryouts";
    public const string Tournaments = "tournaments";
    public const string PickupOpportunities = "pickup_opportunities";
    public const string ForSaleItems = "for_sale_items";
    public const string RosterOpenings = "roster_openings";
    public const string CampsAndClinics = "camps_and_clinics";
    public const string TeamNewListings = "team_new_listings";

    public static IReadOnlyCollection<string> All { get; } =
    [
        Tryouts,
        Tournaments,
        PickupOpportunities,
        ForSaleItems,
        RosterOpenings,
        CampsAndClinics,
        TeamNewListings
    ];
}

public sealed record DashboardActivityPreferenceOptionResponse(
    string Code,
    string Label,
    string Description,
    string Audience,
    bool IsSelected,
    bool IsAvailable,
    string? UnavailableReason);

public sealed record DashboardActivityPreferencesResponse(
    DateTime? LastViewedAt,
    IReadOnlyCollection<string> SelectedActivityTypes,
    IReadOnlyCollection<DashboardActivityPreferenceOptionResponse> Options);

public sealed class UpdateDashboardActivityPreferencesRequest
{
    public IReadOnlyCollection<string> ActivityTypes { get; init; } = [];
}

public sealed record DashboardRecentActivityResponse(
    DateTime ViewedAt,
    DateTime Since,
    DateTime? PreviousViewedAt,
    IReadOnlyCollection<string> EffectiveActivityTypes,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    IReadOnlyCollection<DashboardActivitySectionResponse> Sections)
{
    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;

    public int FirstItemNumber => TotalCount == 0 ? 0 : ((Page - 1) * PageSize) + 1;

    public int LastItemNumber => Math.Min(Page * PageSize, TotalCount);
}

public sealed record DashboardActivitySectionResponse(
    string Code,
    string Title,
    string EmptyMessage,
    int TotalCount,
    IReadOnlyCollection<DashboardActivityItemResponse> Items);

public sealed record DashboardActivityItemResponse(
    string ActivityType,
    string ActivityTypeLabel,
    string ItemKind,
    Guid ItemId,
    string Title,
    string? Subtitle,
    string? Detail,
    string? Location,
    string? Url,
    DateTime ActivityAt);

public sealed record DashboardActivityViewedResponse(DateTime ViewedAt);
