using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Listings;
using TryOutSpot.Web.Models.Moderation;

namespace TryOutSpot.Web.Controllers;

/// <summary>
/// Platform administrator endpoints for listing moderation and listing review.
/// </summary>
[ApiController]
[Authorize(Roles = TryOutSpotRoles.PlatformAdmin)]
[Tags("Admin Listings")]
[Produces("application/json")]
[Route("api/admin/listings")]
public sealed class AdminListingsApiController(AppDbContext dbContext) : ControllerBase
{
    /// <summary>
    /// Lists reported player listings and team opportunities.
    /// </summary>
    [HttpGet("reports")]
    [ProducesResponseType<AdminListingReportListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminListingReportListResponse>> ListReports(
        [FromQuery] string? status,
        [FromQuery] string? targetType,
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var normalizedStatus = NormalizeReportStatusOrAddModelError(status, nameof(status), optional: true);
        var normalizedTargetType = NormalizeTargetTypeOrAddModelError(targetType, nameof(targetType), optional: true);
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.ListingReports.AsNoTracking();
        if (normalizedStatus is not null)
        {
            query = query.Where(report => report.Status == normalizedStatus);
        }

        if (string.Equals(normalizedTargetType, TryOutSpotListingReportTargetTypes.PlayerListing, StringComparison.Ordinal))
        {
            query = query.Where(report => report.PlayerListingId != null);
        }
        else if (string.Equals(normalizedTargetType, TryOutSpotListingReportTargetTypes.TeamOpportunity, StringComparison.Ordinal))
        {
            query = query.Where(report => report.OpportunityId != null);
        }

        var normalizedSearch = NormalizeOptional(q);
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var search = normalizedSearch.ToLowerInvariant();
            query = query.Where(report =>
                report.Reason.ToLower().Contains(search)
                || (report.Details != null && report.Details.ToLower().Contains(search))
                || report.ReporterUser.Email!.ToLower().Contains(search)
                || report.ReporterUser.FirstName.ToLower().Contains(search)
                || report.ReporterUser.LastName.ToLower().Contains(search)
                || (report.PlayerListing != null && report.PlayerListing.Title.ToLower().Contains(search))
                || (report.Opportunity != null && report.Opportunity.Title.ToLower().Contains(search)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var reports = await query
            .Include(report => report.ReporterUser)
            .Include(report => report.ReviewedByUser)
            .Include(report => report.PlayerListing)
            .Include(report => report.Opportunity)
            .OrderBy(report => report.Status == TryOutSpotListingReportStatuses.Pending ? 0
                : report.Status == TryOutSpotListingReportStatuses.InReview ? 1
                : 2)
            .ThenByDescending(report => report.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        return Ok(new AdminListingReportListResponse(
            reports.Select(ToReportSummary).ToArray(),
            page,
            pageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    /// <summary>
    /// Returns a reported listing with reporter and listing owner context.
    /// </summary>
    [HttpGet("reports/{reportId:guid}")]
    [ProducesResponseType<AdminListingReportDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminListingReportDetailResponse>> GetReport(
        Guid reportId,
        CancellationToken cancellationToken = default)
    {
        var report = await LoadReportDetailQuery()
            .AsNoTracking()
            .SingleOrDefaultAsync(currentReport => currentReport.Id == reportId, cancellationToken);
        if (report is null)
        {
            return NotFound();
        }

        return Ok(await ToReportDetailAsync(report, cancellationToken));
    }

    /// <summary>
    /// Updates administrator review status and notes for a listing report.
    /// </summary>
    [HttpPost("reports/{reportId:guid}/review")]
    [ProducesResponseType<AdminListingReportDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminListingReportDetailResponse>> ReviewReport(
        Guid reportId,
        ReviewListingReportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var adminUserId))
        {
            return Unauthorized();
        }

        var normalizedStatus = NormalizeReportStatusOrAddModelError(request.Status, nameof(request.Status), optional: false);
        if (!ModelState.IsValid || normalizedStatus is null)
        {
            return ValidationProblem(ModelState);
        }

        var report = await dbContext.ListingReports
            .SingleOrDefaultAsync(currentReport => currentReport.Id == reportId, cancellationToken);
        if (report is null)
        {
            return NotFound();
        }

        var now = DateTime.UtcNow;
        report.Status = normalizedStatus;
        report.AdminNotes = NormalizeOptional(request.AdminNotes);
        report.ReviewedByUserId = adminUserId;
        report.ReviewedAt = now;
        report.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        var updatedReport = await LoadReportDetailQuery()
            .AsNoTracking()
            .SingleAsync(currentReport => currentReport.Id == reportId, cancellationToken);

        return Ok(await ToReportDetailAsync(updatedReport, cancellationToken));
    }

    /// <summary>
    /// Lists all player-side listings for platform review.
    /// </summary>
    [HttpGet("player-listings")]
    [ProducesResponseType<AdminListingListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminListingListResponse>> ListPlayerListings(
        [FromQuery] string? q,
        [FromQuery] Guid? ownerUserId,
        [FromQuery] bool? isPublished,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.PlayerListings.AsNoTracking();
        if (ownerUserId.HasValue)
        {
            query = query.Where(listing => listing.UserId == ownerUserId.Value);
        }

        if (isPublished.HasValue)
        {
            query = query.Where(listing => listing.IsPublished == isPublished.Value);
        }

        if (isActive.HasValue)
        {
            query = query.Where(listing => listing.IsActive == isActive.Value);
        }

        var normalizedSearch = NormalizeOptional(q);
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var search = normalizedSearch.ToLowerInvariant();
            query = query.Where(listing =>
                listing.Title.ToLower().Contains(search)
                || (listing.Description != null && listing.Description.ToLower().Contains(search))
                || listing.User.Email!.ToLower().Contains(search)
                || listing.User.FirstName.ToLower().Contains(search)
                || listing.User.LastName.ToLower().Contains(search)
                || (listing.Player != null && listing.Player.FirstName.ToLower().Contains(search))
                || (listing.Player != null && listing.Player.LastName.ToLower().Contains(search)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var listings = await query
            .OrderByDescending(listing => listing.UpdatedAt)
            .ThenBy(listing => listing.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(listing => new AdminListingSummaryResponse(
                TryOutSpotListingReportTargetTypes.PlayerListing,
                listing.Id,
                listing.Title,
                listing.ListingType,
                listing.Sport == null ? null : listing.Sport.Name,
                listing.UserId,
                (listing.User.FirstName + " " + listing.User.LastName).Trim(),
                listing.User.Email,
                null,
                null,
                listing.IsPublished,
                listing.IsSearchable,
                listing.IsActive,
                listing.ListingReports.Count(report => report.Status == TryOutSpotListingReportStatuses.Pending
                    || report.Status == TryOutSpotListingReportStatuses.InReview),
                listing.ListingReports.Count,
                listing.CreatedAt,
                listing.UpdatedAt))
            .ToArrayAsync(cancellationToken);

        return Ok(new AdminListingListResponse(
            listings,
            page,
            pageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    /// <summary>
    /// Lists all team-side opportunity listings for platform review.
    /// </summary>
    [HttpGet("team-opportunities")]
    [ProducesResponseType<AdminListingListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminListingListResponse>> ListTeamOpportunities(
        [FromQuery] string? q,
        [FromQuery] Guid? teamId,
        [FromQuery] bool? isPublished,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.Opportunities.AsNoTracking();
        if (teamId.HasValue)
        {
            query = query.Where(opportunity => opportunity.TeamId == teamId.Value);
        }

        if (isPublished.HasValue)
        {
            query = query.Where(opportunity => opportunity.IsPublished == isPublished.Value);
        }

        if (isActive.HasValue)
        {
            query = query.Where(opportunity => opportunity.IsActive == isActive.Value);
        }

        var normalizedSearch = NormalizeOptional(q);
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var search = normalizedSearch.ToLowerInvariant();
            query = query.Where(opportunity =>
                opportunity.Title.ToLower().Contains(search)
                || (opportunity.Description != null && opportunity.Description.ToLower().Contains(search))
                || opportunity.Team.Name.ToLower().Contains(search)
                || (opportunity.Team.Organization != null && opportunity.Team.Organization.Name.ToLower().Contains(search)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var opportunities = await query
            .OrderByDescending(opportunity => opportunity.UpdatedAt)
            .ThenBy(opportunity => opportunity.Title)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(opportunity => new AdminListingSummaryResponse(
                TryOutSpotListingReportTargetTypes.TeamOpportunity,
                opportunity.Id,
                opportunity.Title,
                opportunity.Type,
                opportunity.Sport.Name,
                null,
                null,
                null,
                opportunity.TeamId,
                opportunity.Team.Name,
                opportunity.IsPublished,
                opportunity.Team.IsSearchable,
                opportunity.IsActive,
                opportunity.ListingReports.Count(report => report.Status == TryOutSpotListingReportStatuses.Pending
                    || report.Status == TryOutSpotListingReportStatuses.InReview),
                opportunity.ListingReports.Count,
                opportunity.CreatedAt,
                opportunity.UpdatedAt))
            .ToArrayAsync(cancellationToken);

        return Ok(new AdminListingListResponse(
            opportunities,
            page,
            pageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    private IQueryable<ListingReport> LoadReportDetailQuery()
    {
        return dbContext.ListingReports
            .Include(report => report.ReporterUser)
            .Include(report => report.ReviewedByUser)
            .Include(report => report.PlayerListing!)
                .ThenInclude(listing => listing.User)
            .Include(report => report.PlayerListing!)
                .ThenInclude(listing => listing.Player)
            .Include(report => report.PlayerListing!)
                .ThenInclude(listing => listing.Sport)
            .Include(report => report.Opportunity!)
                .ThenInclude(opportunity => opportunity.Sport)
            .Include(report => report.Opportunity!)
                .ThenInclude(opportunity => opportunity.Team)
                    .ThenInclude(team => team.Organization)
            .Include(report => report.Opportunity!)
                .ThenInclude(opportunity => opportunity.Team)
                    .ThenInclude(team => team.UserTeamRoles)
                        .ThenInclude(teamRole => teamRole.User);
    }

    private async Task<AdminListingReportDetailResponse> ToReportDetailAsync(
        ListingReport report,
        CancellationToken cancellationToken)
    {
        var reportCounts = await GetReportCountsAsync(report.PlayerListingId, report.OpportunityId, cancellationToken);
        var playerListing = report.PlayerListing is null
            ? null
            : ToPlayerListingDetail(report.PlayerListing, reportCounts.OpenCount, reportCounts.TotalCount);
        var teamOpportunity = report.Opportunity is null
            ? null
            : ToTeamOpportunityDetail(report.Opportunity, reportCounts.OpenCount, reportCounts.TotalCount);

        return new AdminListingReportDetailResponse(
            ToReportSummary(report),
            ToUserProfileSummary(report.ReporterUser),
            report.ReviewedByUser is null ? null : ToUserProfileSummary(report.ReviewedByUser),
            playerListing,
            teamOpportunity);
    }

    private async Task<(int OpenCount, int TotalCount)> GetReportCountsAsync(
        Guid? playerListingId,
        Guid? opportunityId,
        CancellationToken cancellationToken)
    {
        var query = dbContext.ListingReports.AsNoTracking();
        if (playerListingId.HasValue)
        {
            query = query.Where(report => report.PlayerListingId == playerListingId.Value);
        }
        else if (opportunityId.HasValue)
        {
            query = query.Where(report => report.OpportunityId == opportunityId.Value);
        }
        else
        {
            return (0, 0);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var openCount = await query
            .CountAsync(report => report.Status == TryOutSpotListingReportStatuses.Pending
                || report.Status == TryOutSpotListingReportStatuses.InReview, cancellationToken);

        return (openCount, totalCount);
    }

    private string? NormalizeReportStatusOrAddModelError(string? status, string modelStateKey, bool optional)
    {
        if (optional && string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        var normalizedStatus = TryOutSpotListingReportStatuses.Normalize(status);
        if (normalizedStatus is null)
        {
            ModelState.AddModelError(
                modelStateKey,
                $"'{status}' is not a supported report status. Supported values: {string.Join(", ", TryOutSpotListingReportStatuses.Values)}.");
        }

        return normalizedStatus;
    }

    private string? NormalizeTargetTypeOrAddModelError(string? targetType, string modelStateKey, bool optional)
    {
        if (optional && string.IsNullOrWhiteSpace(targetType))
        {
            return null;
        }

        var normalizedTargetType = TryOutSpotListingReportTargetTypes.Normalize(targetType);
        if (normalizedTargetType is null)
        {
            ModelState.AddModelError(
                modelStateKey,
                $"'{targetType}' is not a supported listing report target type. Supported values: {string.Join(", ", TryOutSpotListingReportTargetTypes.Values)}.");
        }

        return normalizedTargetType;
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdClaim, out userId);
    }

    private static AdminListingReportSummaryResponse ToReportSummary(ListingReport report)
    {
        var targetType = report.PlayerListingId.HasValue
            ? TryOutSpotListingReportTargetTypes.PlayerListing
            : TryOutSpotListingReportTargetTypes.TeamOpportunity;
        var targetId = report.PlayerListingId ?? report.OpportunityId ?? Guid.Empty;
        var targetTitle = report.PlayerListing?.Title
            ?? report.Opportunity?.Title
            ?? string.Empty;

        return new AdminListingReportSummaryResponse(
            report.Id,
            targetType,
            targetId,
            targetTitle,
            report.ReporterUserId,
            GetDisplayName(report.ReporterUser),
            report.ReporterUser.Email ?? string.Empty,
            report.Reason,
            report.Details,
            report.Status,
            report.AdminNotes,
            report.CreatedAt,
            report.UpdatedAt,
            report.ReviewedByUserId,
            report.ReviewedByUser is null ? null : GetDisplayName(report.ReviewedByUser),
            report.ReviewedAt);
    }

    private static AdminPlayerListingDetailResponse ToPlayerListingDetail(
        PlayerListing listing,
        int openReportCount,
        int totalReportCount)
    {
        return new AdminPlayerListingDetailResponse(
            listing.Id,
            listing.UserId,
            GetDisplayName(listing.User),
            listing.User.Email ?? string.Empty,
            listing.ListingType,
            listing.Title,
            listing.Description,
            listing.PlayerId,
            listing.Player is null ? null : $"{listing.Player.FirstName} {listing.Player.LastName}".Trim(),
            listing.Sport?.Name,
            listing.City,
            listing.State,
            listing.ZipCode,
            listing.IsPublished,
            listing.IsSearchable,
            listing.IsActive,
            openReportCount,
            totalReportCount,
            listing.CreatedAt,
            listing.UpdatedAt);
    }

    private static AdminTeamOpportunityDetailResponse ToTeamOpportunityDetail(
        Opportunity opportunity,
        int openReportCount,
        int totalReportCount)
    {
        var representatives = opportunity.Team.UserTeamRoles
            .Where(teamRole => teamRole.IsActive)
            .OrderBy(teamRole => teamRole.User.Email)
            .Select(teamRole => ToUserProfileSummary(teamRole.User))
            .ToArray();

        return new AdminTeamOpportunityDetailResponse(
            opportunity.Id,
            opportunity.TeamId,
            opportunity.Team.Name,
            opportunity.Team.Organization?.Name,
            opportunity.Type,
            opportunity.Title,
            opportunity.Description,
            opportunity.Sport.Name,
            opportunity.City,
            opportunity.State,
            opportunity.ZipCode,
            opportunity.IsPublished,
            opportunity.IsActive,
            openReportCount,
            totalReportCount,
            representatives,
            opportunity.CreatedAt,
            opportunity.UpdatedAt);
    }

    private static AdminUserProfileSummaryResponse ToUserProfileSummary(User user)
    {
        return new AdminUserProfileSummaryResponse(
            user.Id,
            user.Email ?? string.Empty,
            user.FirstName,
            user.LastName,
            GetDisplayName(user),
            user.IsActive);
    }

    private static string GetDisplayName(User user)
    {
        return $"{user.FirstName} {user.LastName}".Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
