namespace TryOutSpot.Web.Listings;

public static class TryOutSpotListingReportStatuses
{
    public const string Pending = "Pending";
    public const string InReview = "InReview";
    public const string Reviewed = "Reviewed";
    public const string Dismissed = "Dismissed";
    public const string ActionTaken = "ActionTaken";

    public static readonly string[] Values =
    [
        Pending,
        InReview,
        Reviewed,
        Dismissed,
        ActionTaken
    ];

    public static readonly string[] OpenStatuses =
    [
        Pending,
        InReview
    ];

    public static string? Normalize(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        var normalized = status.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal);
        return normalized.ToLowerInvariant() switch
        {
            "pending" => Pending,
            "inreview" or "reviewing" => InReview,
            "reviewed" => Reviewed,
            "dismissed" => Dismissed,
            "actiontaken" or "resolved" => ActionTaken,
            _ => null
        };
    }
}
