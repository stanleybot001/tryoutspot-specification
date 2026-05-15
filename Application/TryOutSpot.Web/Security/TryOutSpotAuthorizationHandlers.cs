using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Security;

public sealed class ActiveUserAuthorizationHandler(UserManager<User> userManager)
    : AuthorizationHandler<ActiveUserRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveUserRequirement requirement)
    {
        var user = await FindCurrentUserAsync(context.User, userManager);
        if (user is { IsActive: true })
        {
            context.Succeed(requirement);
        }
    }

    internal static Task<User?> FindCurrentUserAsync(ClaimsPrincipal principal, UserManager<User> userManager)
    {
        var userIdClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdClaim, out var userId)
            ? userManager.FindByIdAsync(userId.ToString())
            : Task.FromResult<User?>(null);
    }
}

public sealed class ConfirmedEmailAuthorizationHandler(UserManager<User> userManager)
    : AuthorizationHandler<ConfirmedEmailRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ConfirmedEmailRequirement requirement)
    {
        var user = await ActiveUserAuthorizationHandler.FindCurrentUserAsync(context.User, userManager);
        if (user is { IsActive: true, EmailConfirmed: true })
        {
            context.Succeed(requirement);
        }
    }
}

public sealed class FeatureAccessAuthorizationHandler(IEntitlementService entitlementService)
    : AuthorizationHandler<FeatureAccessRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        FeatureAccessRequirement requirement)
    {
        var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return;
        }

        if (await entitlementService.HasFeatureAsync(userId, requirement.FeatureCode, CancellationToken.None))
        {
            context.Succeed(requirement);
        }
    }
}

public sealed class AnyFeatureAccessAuthorizationHandler(IEntitlementService entitlementService)
    : AuthorizationHandler<AnyFeatureAccessRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AnyFeatureAccessRequirement requirement)
    {
        if (requirement.FeatureCodes.Count == 0)
        {
            return;
        }

        var userIdClaim = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return;
        }

        var entitlements = await entitlementService.GetEntitlementsAsync(userId, CancellationToken.None);
        if (entitlements is null)
        {
            return;
        }

        if (requirement.FeatureCodes.Any(featureCode =>
                entitlements.FeatureCodes.Contains(featureCode, StringComparer.Ordinal)))
        {
            context.Succeed(requirement);
        }
    }
}
