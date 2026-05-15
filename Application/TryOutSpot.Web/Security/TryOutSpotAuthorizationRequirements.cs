using Microsoft.AspNetCore.Authorization;

namespace TryOutSpot.Web.Security;

public sealed class ActiveUserRequirement : IAuthorizationRequirement;

public sealed class ConfirmedEmailRequirement : IAuthorizationRequirement;

public sealed class FeatureAccessRequirement(string featureCode) : IAuthorizationRequirement
{
    public string FeatureCode { get; } = featureCode;
}

public sealed class AnyFeatureAccessRequirement(params string[] featureCodes) : IAuthorizationRequirement
{
    public IReadOnlyCollection<string> FeatureCodes { get; } = featureCodes
        .Where(featureCode => !string.IsNullOrWhiteSpace(featureCode))
        .Distinct(StringComparer.Ordinal)
        .ToArray();
}
