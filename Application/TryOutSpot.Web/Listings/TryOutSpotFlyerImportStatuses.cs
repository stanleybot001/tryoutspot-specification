namespace TryOutSpot.Web.Listings;

public static class TryOutSpotFlyerImportStatuses
{
    public const string PendingReview = "pending_review";
    public const string DraftCreated = "draft_created";
    public const string Published = "published";
    public const string Rejected = "rejected";

    public static readonly string[] Values =
    [
        PendingReview,
        DraftCreated,
        Published,
        Rejected
    ];

    public static string? Normalize(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        var normalized = status.Trim().ToLowerInvariant().Replace('-', '_');
        return Values.Contains(normalized, StringComparer.Ordinal) ? normalized : null;
    }
}
