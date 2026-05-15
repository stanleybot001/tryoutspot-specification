namespace TryOutSpot.Web.Listings;

public static class TryOutSpotPlayerListingTypes
{
    public const string PickupPlayer = "pickup_player";
    public const string LookingForTeam = "looking_for_team";
    public const string UsedEquipment = "used_equipment";
    public const string WantedEquipment = "wanted_equipment";
    public const string PrivateLessons = "private_lessons";
    public const string TrainingPartner = "training_partner";

    private static readonly string[] SupportedTypes =
    [
        PickupPlayer,
        LookingForTeam,
        UsedEquipment,
        WantedEquipment,
        PrivateLessons,
        TrainingPartner
    ];

    public static IReadOnlyCollection<string> Values => SupportedTypes;

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim().Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
        return SupportedTypes.Contains(normalized, StringComparer.Ordinal) ? normalized : null;
    }
}
