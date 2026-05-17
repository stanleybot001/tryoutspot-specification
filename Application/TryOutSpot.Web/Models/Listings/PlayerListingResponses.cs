namespace TryOutSpot.Web.Models.Listings;

/// <summary>
/// Paginated listing search results.
/// </summary>
public sealed record PlayerListingListResponse(
    IReadOnlyCollection<PlayerListingSummaryResponse> Listings,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    string? SearchOriginZipCode = null,
    int? SearchRadiusMiles = null,
    bool AdvancedFiltersApplied = false);

/// <summary>
/// Listing summary used by search and owner listing views.
/// </summary>
public sealed record PlayerListingSummaryResponse(
    Guid Id,
    string ListingType,
    string Title,
    string? Description,
    Guid? PlayerId,
    string? PlayerName,
    Guid? SportId,
    string? SportName,
    decimal? AskingPrice,
    string? Currency,
    string? Condition,
    string? City,
    string? State,
    string? ZipCode,
    bool IsPublished,
    bool IsSearchable,
    DateTime? PublishedAt,
    DateTime? ExpiresAt,
    DateTime UpdatedAt,
    double? DistanceMiles = null,
    bool IsFavorited = false);

/// <summary>
/// Single listing result.
/// </summary>
public sealed record PlayerListingDetailResponse(
    Guid Id,
    Guid UserId,
    string ListingType,
    string Title,
    string? Description,
    Guid? PlayerId,
    string? PlayerName,
    Guid? SportId,
    string? SportName,
    decimal? AskingPrice,
    string? Currency,
    string? Condition,
    string? City,
    string? State,
    string? ZipCode,
    bool IsPublished,
    bool IsSearchable,
    DateTime? PublishedAt,
    DateTime? ExpiresAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
/// Simple action response for listing writes.
/// </summary>
public sealed record PlayerListingActionResponse(
    string Message,
    PlayerListingDetailResponse Listing);
