using TryOutSpot.Web.Billing;

namespace TryOutSpot.Web.Security;

public static class TryOutSpotAuthorizationPolicies
{
    public const string ActiveUser = "TryOutSpot.ActiveUser";

    public const string ConfirmedEmail = "TryOutSpot.ConfirmedEmail";

    public const string ManagePlayerProfile = "TryOutSpot.ManagePlayerProfile";

    public const string ManageTeamProfile = "TryOutSpot.ManageTeamProfile";

    public const string FeaturePolicyPrefix = "TryOutSpot.Feature:";

    public const string BasicPlayerProfileFeaturePolicy =
        FeaturePolicyPrefix + TryOutSpotFeatureCodes.CreateBasicPlayerProfiles;

    public static string Feature(string featureCode)
    {
        return $"{FeaturePolicyPrefix}{featureCode}";
    }

    public static bool TryGetFeatureCode(string policyName, out string featureCode)
    {
        if (policyName.StartsWith(FeaturePolicyPrefix, StringComparison.Ordinal)
            && policyName.Length > FeaturePolicyPrefix.Length)
        {
            featureCode = policyName[FeaturePolicyPrefix.Length..];
            return true;
        }

        featureCode = string.Empty;
        return false;
    }
}
