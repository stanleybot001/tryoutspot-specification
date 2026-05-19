using System.Security.Claims;
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
            status.PromotionName,
            status.ClaimedCount,
            status.RemainingCount,
            status.MaxClaims,
            status.GrantMonths,
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

    [HttpPost("launch-founder-offer/settings")]
    [ProducesResponseType<LaunchPromotionStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<LaunchPromotionStatusResponse>> UpdateLaunchFounderOfferSettings(
        LaunchPromotionSettingsRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var adminUserId))
        {
            return Unauthorized();
        }

        if (!ValidatePromotionSettings(request))
        {
            return ValidationProblem(ModelState);
        }

        await launchPromotionStatusService.UpdateLaunchFounderOfferSettingsAsync(
            request.Name,
            request.MaxRedemptions,
            request.GrantMonths,
            adminUserId,
            cancellationToken);

        return await GetLaunchFounderOfferStatus(cancellationToken: cancellationToken);
    }

    [HttpPost("launch-founder-offer/reset")]
    [ProducesResponseType<LaunchPromotionStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<LaunchPromotionStatusResponse>> ResetLaunchFounderOffer(
        LaunchPromotionSettingsRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var adminUserId))
        {
            return Unauthorized();
        }

        if (!ValidatePromotionSettings(request))
        {
            return ValidationProblem(ModelState);
        }

        await launchPromotionStatusService.ResetLaunchFounderOfferAsync(
            request.Name,
            request.MaxRedemptions,
            request.GrantMonths,
            adminUserId,
            cancellationToken);

        return await GetLaunchFounderOfferStatus(cancellationToken: cancellationToken);
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }

    private bool ValidatePromotionSettings(LaunchPromotionSettingsRequest request)
    {
        if (request.MaxRedemptions is < LaunchPromotionStatusService.MinMaxRedemptions
            or > LaunchPromotionStatusService.MaxMaxRedemptions)
        {
            ModelState.AddModelError(nameof(request.MaxRedemptions), "Promotion claim limit must be between 1 and 100000.");
        }

        if (request.GrantMonths is < LaunchPromotionStatusService.MinGrantMonths
            or > LaunchPromotionStatusService.MaxGrantMonths)
        {
            ModelState.AddModelError(nameof(request.GrantMonths), "Promotion duration must be between 1 and 120 months.");
        }

        if (!string.IsNullOrWhiteSpace(request.Name) && request.Name.Length > 200)
        {
            ModelState.AddModelError(nameof(request.Name), "Promotion name must be 200 characters or fewer.");
        }

        return ModelState.IsValid;
    }
}
