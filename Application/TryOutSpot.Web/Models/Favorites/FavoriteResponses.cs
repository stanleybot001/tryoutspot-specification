namespace TryOutSpot.Web.Models.Favorites;

/// <summary>
/// Favorites saved by the signed-in account.
/// </summary>
public sealed record FavoriteListResponse(
    IReadOnlyCollection<PlayerListingFavoriteSummaryResponse> PlayerListings,
    IReadOnlyCollection<OpportunityFavoriteSummaryResponse> Opportunities);

/// <summary>
/// Saved player listing favorite summary.
/// </summary>
public sealed record PlayerListingFavoriteSummaryResponse(
    Guid FavoriteId,
    Guid ListingId,
    string ListingType,
    string Title,
    string? PlayerName,
    string? SportName,
    string? City,
    string? State,
    string? ZipCode,
    DateTime CreatedAt);

/// <summary>
/// Saved opportunity favorite summary.
/// </summary>
public sealed record OpportunityFavoriteSummaryResponse(
    Guid FavoriteId,
    Guid OpportunityId,
    Guid TeamId,
    string TeamName,
    string? OrganizationName,
    string SportName,
    string Type,
    string Title,
    DateTime? EventDate,
    string? City,
    string? State,
    string? ZipCode,
    DateTime CreatedAt);

/// <summary>
/// Simple action response for favorite add/remove requests.
/// </summary>
public sealed record FavoriteActionResponse(
    string Message,
    bool IsFavorited);
