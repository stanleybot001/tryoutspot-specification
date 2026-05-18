namespace TryOutSpot.Web.Listings;

public static class TryOutSpotListingReportTargetTypes
{
    public const string PlayerListing = "PlayerListing";
    public const string TeamOpportunity = "TeamOpportunity";

    public static readonly string[] Values =
    [
        PlayerListing,
        TeamOpportunity
    ];

    public static string? Normalize(string? targetType)
    {
        if (string.IsNullOrWhiteSpace(targetType))
        {
            return null;
        }

        var normalized = targetType.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal);
        return normalized.ToLowerInvariant() switch
        {
            "playerlisting" or "player" or "players" => PlayerListing,
            "teamopportunity" or "opportunity" or "opportunities" or "teamlisting" => TeamOpportunity,
            _ => null
        };
    }
}
