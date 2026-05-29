using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TryOutSpot.Web.Models.Dashboard;
using TryOutSpot.Web.Security;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Controllers;

/// <summary>
/// Personalized dashboard activity endpoints.
/// </summary>
[ApiController]
[Tags("Dashboard")]
[Produces("application/json")]
[Route("api/dashboard")]
[Authorize(Policy = TryOutSpotAuthorizationPolicies.ActiveUser)]
public sealed class DashboardApiController(
    IDashboardActivityService dashboardActivityService,
    IActivationAssistanceService activationAssistanceService) : ControllerBase
{
    /// <summary>
    /// Returns recent dashboard activity using the signed-in account's entitlements and preferences.
    /// </summary>
    [HttpGet("recent-activity")]
    [ProducesResponseType<DashboardRecentActivityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<DashboardRecentActivityResponse>> RecentActivity(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        return Ok(await dashboardActivityService.GetRecentActivityAsync(
            userId,
            page,
            pageSize,
            cancellationToken));
    }

    /// <summary>
    /// Marks the signed-in account's dashboard activity feed as viewed.
    /// </summary>
    [HttpPost("recent-activity/viewed")]
    [ProducesResponseType<DashboardActivityViewedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<DashboardActivityViewedResponse>> MarkRecentActivityViewed(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        return Ok(await dashboardActivityService.MarkViewedAsync(userId, cancellationToken));
    }

    /// <summary>
    /// Returns the signed-in account's activation assistance prompt eligibility.
    /// </summary>
    [HttpGet("activation-assistance")]
    [ProducesResponseType<ActivationAssistancePromptResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ActivationAssistancePromptResponse>> ActivationAssistance(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        return Ok(await activationAssistanceService.GetTeamFirstListingPromptAsync(userId, cancellationToken));
    }

    /// <summary>
    /// Suppresses the team activation assistance prompt for the signed-in account.
    /// </summary>
    [HttpPost("activation-assistance/dismiss")]
    [ProducesResponseType<ActivationAssistanceActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ActivationAssistanceActionResponse>> DismissActivationAssistance(
        DismissActivationAssistanceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        return Ok(await activationAssistanceService.DismissTeamFirstListingPromptAsync(
            userId,
            request.PromptKey,
            request.TeamId,
            cancellationToken));
    }

    /// <summary>
    /// Returns configurable dashboard activity options for the signed-in account.
    /// </summary>
    [HttpGet("preferences")]
    [ProducesResponseType<DashboardActivityPreferencesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<DashboardActivityPreferencesResponse>> Preferences(
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        return Ok(await dashboardActivityService.GetPreferencesAsync(userId, cancellationToken));
    }

    /// <summary>
    /// Updates dashboard activity filters for the signed-in account.
    /// </summary>
    [HttpPost("preferences")]
    [ProducesResponseType<DashboardActivityPreferencesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<DashboardActivityPreferencesResponse>> UpdatePreferences(
        UpdateDashboardActivityPreferencesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        return Ok(await dashboardActivityService.UpdatePreferencesAsync(
            userId,
            request.ActivityTypes,
            cancellationToken));
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdClaim, out userId);
    }
}
