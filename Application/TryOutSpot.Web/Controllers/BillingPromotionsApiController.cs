using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Models.Billing;
using TryOutSpot.Web.Security;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Controllers;

[ApiController]
[Authorize(Policy = TryOutSpotAuthorizationPolicies.ConfirmedEmail)]
[Tags("Billing")]
[Produces("application/json")]
[Route("api/billing/promotions")]
public sealed class BillingPromotionsApiController(
    UserManager<User> userManager,
    ILaunchPromotionStatusService launchPromotionStatusService) : ControllerBase
{
    [HttpGet("launch-founder-offer/status")]
    [ProducesResponseType<LaunchPromotionAvailabilityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LaunchPromotionAvailabilityResponse>> GetLaunchFounderOfferStatus(
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null)
        {
            return Unauthorized();
        }

        var roles = TryOutSpotRoles.CanonicalizeRoleSet(await userManager.GetRolesAsync(user));
        var availability = await launchPromotionStatusService.GetLaunchFounderOfferAvailabilityAsync(
            user.Id,
            roles,
            cancellationToken);

        return Ok(new LaunchPromotionAvailabilityResponse(
            availability.PromotionCode,
            availability.PromotionName,
            availability.IsEnabled,
            availability.IsEligibleForCurrentAccountType,
            availability.CanClaim,
            availability.HasAlreadyClaimed,
            availability.IsExhausted,
            availability.HasActiveAccessForEligiblePlans,
            availability.ClaimedCount,
            availability.RemainingCount,
            availability.MaxClaims,
            availability.GrantMonths,
            availability.ExistingClaimEndsAtUtc,
            availability.EligiblePlanCodes));
    }

    [HttpPost("launch-founder-offer/claim")]
    [ProducesResponseType<PromotionClaimResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PromotionClaimResponse>> ClaimLaunchFounderOffer(
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentActiveUserAsync();
        if (user is null)
        {
            return Unauthorized();
        }

        var roles = TryOutSpotRoles.CanonicalizeRoleSet(await userManager.GetRolesAsync(user));
        var result = await launchPromotionStatusService.ClaimLaunchFounderOfferAsync(
            user.Id,
            roles,
            cancellationToken);

        return result.Status switch
        {
            LaunchPromotionClaimResultStatus.Claimed => Ok(ToPromotionClaimResponse(result)),
            LaunchPromotionClaimResultStatus.AlreadyClaimed => Ok(ToPromotionClaimResponse(result, claimed: false)),
            LaunchPromotionClaimResultStatus.NoEligibleAccountType => InvalidAccountTypePromotionResult(),
            LaunchPromotionClaimResultStatus.AlreadyHasAccess => AlreadyHasPromotionAccessResult(),
            LaunchPromotionClaimResultStatus.UserNotFound => Unauthorized(),
            _ => Conflict(ToPromotionClaimResponse(result, claimed: false))
        };
    }

    private async Task<User?> GetCurrentActiveUserAsync()
    {
        var user = await userManager.GetUserAsync(User);

        return user is { IsActive: true } ? user : null;
    }

    private ActionResult<PromotionClaimResponse> InvalidAccountTypePromotionResult()
    {
        ModelState.AddModelError("accountTypes", "This promotion requires a parent, player, or team representative account type.");
        return ValidationProblem(ModelState);
    }

    private ActionResult<PromotionClaimResponse> AlreadyHasPromotionAccessResult()
    {
        ModelState.AddModelError("promotion", "This account already has active access for the launch promotion plans.");
        return ValidationProblem(ModelState);
    }

    private static PromotionClaimResponse ToPromotionClaimResponse(
        LaunchPromotionClaimResult result,
        bool? claimed = null)
    {
        return new PromotionClaimResponse(
            result.PromotionCode,
            claimed ?? result.Status == LaunchPromotionClaimResultStatus.Claimed,
            result.EndsAt,
            result.Grants);
    }
}
