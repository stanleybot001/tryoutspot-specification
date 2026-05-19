using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Models.Billing;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Controllers;

[ApiController]
[Authorize(Roles = TryOutSpotRoles.PlatformAdmin)]
[Tags("Billing")]
[Produces("application/json")]
[Route("api/admin/billing/promotions")]
public sealed class AdminBillingPromotionsApiController(
    ILaunchPromotionStatusService launchPromotionStatusService) : ControllerBase
{
    [HttpGet("launch-founder-offer/status")]
    [ProducesResponseType<LaunchPromotionStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<LaunchPromotionStatusResponse>> GetLaunchFounderOfferStatus(
        int limit = LaunchPromotionStatusService.DefaultClaimLimit,
        int offset = 0,
        CancellationToken cancellationToken = default)
    {
        var status = await launchPromotionStatusService.GetLaunchFounderOfferStatusAsync(
            limit,
            offset,
            cancellationToken);

        return Ok(new LaunchPromotionStatusResponse(
            status.PromotionCode,
            status.ClaimedCount,
            status.RemainingCount,
            status.MaxClaims,
            status.ActiveGrantCount,
            status.LatestGrantEndsAtUtc,
            status.Limit,
            status.Offset,
            status.RecentClaims.Select(claim => new LaunchPromotionClaimResponse(
                claim.UserId,
                claim.UserDisplayName,
                claim.UserEmail,
                claim.GrantedPlanCodes,
                claim.ActiveGrantCount,
                claim.LatestGrantEndsAtUtc,
                claim.RedeemedAtUtc)).ToArray()));
    }
}
