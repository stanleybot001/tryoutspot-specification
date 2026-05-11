using Microsoft.AspNetCore.Authorization;

namespace TryOutSpot.Web.Security;

public sealed class ActiveUserRequirement : IAuthorizationRequirement;

public sealed class ConfirmedEmailRequirement : IAuthorizationRequirement;

public sealed class FeatureAccessRequirement(string featureCode) : IAuthorizationRequirement
{
    public string FeatureCode { get; } = featureCode;
}
