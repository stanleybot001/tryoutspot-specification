namespace TryOutSpot.Web.Listings;

public static class TryOutSpotRecruitingPlatforms
{
    public const string OtherCode = "other";

    public const int MaxLinks = 8;

    public sealed record Platform(string Code, string Name, string[] Domains);

    public static readonly Platform[] All =
    [
        new("fieldlevel", "FieldLevel", ["fieldlevel.com"]),
        new("ncsa", "NCSA", ["ncsasports.org"]),
        new("sportsrecruits", "SportsRecruits", ["sportsrecruits.com"]),
        new("perfectgame", "Perfect Game", ["perfectgame.org"]),
        new("prepbaseball", "Prep Baseball", ["prepbaseballreport.com", "prepbaseball.com"]),
        new("extrainnings", "Extra Innings Softball", ["extrainningssoftball.com"]),
        new("softballfactory", "Softball Factory", ["softballfactory.com"]),
        new("gamechanger", "GameChanger", ["gc.com", "gamechanger.io"]),
        new("hudl", "Hudl", ["hudl.com"]),
        new("maxpreps", "MaxPreps", ["maxpreps.com"]),
        new("rapsodo", "Rapsodo", ["rapsodo.com"]),
        new("captainu", "CaptainU", ["captainu.com"]),
        new("berecruited", "BeRecruited", ["berecruited.com"]),
        new("scorability", "Scorability", ["scorability.com"]),
        new("247sports", "247Sports", ["247sports.com"]),
        new("verbalcommits", "Verbal Commits", ["verbalcommits.com"]),
        new("topdrawersoccer", "TopDrawerSoccer", ["topdrawersoccer.com"]),
        new("ballertv", "BallerTV", ["ballertv.com"])
    ];

    public static Platform? Detect(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        return All.FirstOrDefault(platform => platform.Domains.Any(domain =>
            host == domain || host.EndsWith("." + domain, StringComparison.Ordinal)));
    }

    public static string GetLabel(string storedKey, string url)
    {
        var baseCode = StripDuplicateSuffix(storedKey);
        var platform = All.FirstOrDefault(current => string.Equals(current.Code, baseCode, StringComparison.OrdinalIgnoreCase));
        if (platform is not null)
        {
            return platform.Name;
        }

        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host
            : "Recruiting profile";
    }

    private static string StripDuplicateSuffix(string storedKey)
    {
        var separatorIndex = storedKey.LastIndexOf('_');
        return separatorIndex > 0 && int.TryParse(storedKey[(separatorIndex + 1)..], out _)
            ? storedKey[..separatorIndex]
            : storedKey;
    }
}
