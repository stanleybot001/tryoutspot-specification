using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace TryOutSpot.Web.Security;

public sealed class TryOutSpotAuthorizationPolicyProvider(IOptions<AuthorizationOptions> options)
    : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (TryOutSpotAuthorizationPolicies.TryGetFeatureCode(policyName, out var featureCode))
        {
            return BuildAuthenticatedPolicy()
                .AddRequirements(new FeatureAccessRequirement(featureCode))
                .Build();
        }

        return await base.GetPolicyAsync(policyName);
    }

    internal static AuthorizationPolicyBuilder BuildAuthenticatedPolicy()
    {
        return new AuthorizationPolicyBuilder(
                JwtBearerDefaults.AuthenticationScheme,
                TryOutSpotAuthenticationSchemes.WebCookie)
            .RequireAuthenticatedUser();
    }
}
