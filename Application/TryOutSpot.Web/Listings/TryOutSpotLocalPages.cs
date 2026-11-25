using System.Text;

namespace TryOutSpot.Web.Listings;

public static class TryOutSpotLocalPages
{
    public const int DefaultRadiusMiles = 65;

    public static readonly int[] RadiusOptions = [25, 50, 65, 100, 120];

    public static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasSeparator = true;
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (character is '.' or '\'')
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(character))
            {
                builder.Append(character);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator)
            {
                builder.Append('-');
                lastWasSeparator = true;
            }
        }

        return builder.ToString().TrimEnd('-');
    }

    public static string ExpandCitySlugAbbreviations(string citySlug)
    {
        var parts = citySlug.Split('-');
        for (var index = 0; index < parts.Length; index++)
        {
            parts[index] = parts[index] switch
            {
                "st" => "saint",
                "ste" => "sainte",
                "ft" => "fort",
                "mt" => "mount",
                _ => parts[index]
            };
        }

        return string.Join('-', parts);
    }

    public static string BuildCityPath(string state, string city)
    {
        return $"/tryouts/{state.Trim().ToLowerInvariant()}/{Slugify(city)}";
    }

    public static string BuildZipPath(string zipCode)
    {
        return $"/tryouts/near/{zipCode.Trim()}";
    }
}
