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
public sealed class DashboardApiController(IDashboardActivityService dashboardActivityService) : ControllerBase
{
    /// <summary>
    /// Returns recent dashboard activity using the signed-in account's entitlements and preferences.
    /// </summary>
    [HttpGet("recent-activity")]
    [ProducesResponseType<DashboardRecentActivityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<DashboardRecentActivityResponse>> RecentActivity(
        [FromQuery] int takePerSection = 12,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        return Ok(await dashboardActivityService.GetRecentActivityAsync(
            userId,
            markAsViewed: false,
            takePerSection,
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
