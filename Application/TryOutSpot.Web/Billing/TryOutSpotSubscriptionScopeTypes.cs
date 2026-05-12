namespace TryOutSpot.Web.Billing;

public static class TryOutSpotSubscriptionScopeTypes
{
    public const string Account = "account";
    public const string Player = "player";
    public const string Team = "team";
    public const string Organization = "organization";

    public static string? Normalize(string? scopeType)
    {
        if (string.IsNullOrWhiteSpace(scopeType))
        {
            return Account;
        }

        var normalized = scopeType.Trim().Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
        return normalized switch
        {
            Account or "user" or "profile" => Account,
            Player or "athlete" or "child" => Player,
            Team or "club" => Team,
            Organization or "org" or "academy" => Organization,
            _ => null
        };
    }
}
