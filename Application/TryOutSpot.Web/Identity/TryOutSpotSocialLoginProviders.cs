namespace TryOutSpot.Web.Identity;

public static class TryOutSpotSocialLoginProviders
{
    public const string Google = "Google";

    public const string Facebook = "Facebook";

    public const string Apple = "Apple";

    public static readonly string[] All =
    [
        Google,
        Facebook,
        Apple
    ];

    public static string? Normalize(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return null;
        }

        return All.FirstOrDefault(knownProvider =>
            string.Equals(knownProvider, provider.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}
