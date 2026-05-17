namespace TryOutSpot.Web.Listings;

public static class TryOutSpotOpportunityWaiverMethods
{
    public const string AtEvent = "at_event";
    public const string Downloadable = "downloadable";

    public static readonly string[] All = [AtEvent, Downloadable];

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().ToLowerInvariant();
        return All.Contains(normalized, StringComparer.Ordinal)
            ? normalized
            : null;
    }
}
