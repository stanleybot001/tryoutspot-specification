namespace TryOutSpot.Web.Services;

internal static class FlyerLocationNormalization
{
    private static readonly IReadOnlyDictionary<string, string> StateCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Alabama"] = "AL",
        ["Alaska"] = "AK",
        ["Arizona"] = "AZ",
        ["Arkansas"] = "AR",
        ["California"] = "CA",
        ["Colorado"] = "CO",
        ["Connecticut"] = "CT",
        ["Delaware"] = "DE",
        ["District of Columbia"] = "DC",
        ["Florida"] = "FL",
        ["Georgia"] = "GA",
        ["Hawaii"] = "HI",
        ["Idaho"] = "ID",
        ["Illinois"] = "IL",
        ["Indiana"] = "IN",
        ["Iowa"] = "IA",
        ["Kansas"] = "KS",
        ["Kentucky"] = "KY",
        ["Louisiana"] = "LA",
        ["Maine"] = "ME",
        ["Maryland"] = "MD",
        ["Massachusetts"] = "MA",
        ["Michigan"] = "MI",
        ["Minnesota"] = "MN",
        ["Mississippi"] = "MS",
        ["Missouri"] = "MO",
        ["Montana"] = "MT",
        ["Nebraska"] = "NE",
        ["Nevada"] = "NV",
        ["New Hampshire"] = "NH",
        ["New Jersey"] = "NJ",
        ["New Mexico"] = "NM",
        ["New York"] = "NY",
        ["North Carolina"] = "NC",
        ["North Dakota"] = "ND",
        ["Ohio"] = "OH",
        ["Oklahoma"] = "OK",
        ["Oregon"] = "OR",
        ["Pennsylvania"] = "PA",
        ["Rhode Island"] = "RI",
        ["South Carolina"] = "SC",
        ["South Dakota"] = "SD",
        ["Tennessee"] = "TN",
        ["Texas"] = "TX",
        ["Utah"] = "UT",
        ["Vermont"] = "VT",
        ["Virginia"] = "VA",
        ["Washington"] = "WA",
        ["West Virginia"] = "WV",
        ["Wisconsin"] = "WI",
        ["Wyoming"] = "WY"
    };

    public static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static string? NormalizeLength(string? value, int maxLength)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return null;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    public static string? NormalizeZipCode(string? zipCode)
    {
        var normalized = NormalizeOptional(zipCode);
        if (normalized is null)
        {
            return null;
        }

        var digits = new string(normalized.Where(char.IsDigit).ToArray());
        if (digits.Length >= 5)
        {
            return digits[..5];
        }

        return normalized.Length <= 10 ? normalized : normalized[..10];
    }

    public static string? NormalizeState(string? state)
    {
        var normalized = NormalizeOptional(state);
        if (normalized is null)
        {
            return null;
        }

        if (normalized.StartsWith("US-", StringComparison.OrdinalIgnoreCase)
            && normalized.Length >= 5)
        {
            normalized = normalized[3..];
        }

        if (normalized.Length == 2)
        {
            return normalized.ToUpperInvariant();
        }

        return StateCodes.TryGetValue(normalized, out var stateCode)
            ? stateCode
            : NormalizeLength(normalized.ToUpperInvariant(), 2);
    }

    public static string? NormalizeCityKey(string? city)
    {
        var normalized = NormalizeOptional(city);
        if (normalized is null)
        {
            return null;
        }

        var key = new string(normalized
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

        return key.Length == 0 ? null : key;
    }
}
