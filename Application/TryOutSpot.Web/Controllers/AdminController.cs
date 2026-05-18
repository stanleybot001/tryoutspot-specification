using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Listings;
using TryOutSpot.Web.Models.Admin;
using TryOutSpot.Web.Security;

namespace TryOutSpot.Web.Controllers;

[Route("admin")]
[Authorize(
    AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
    Roles = TryOutSpotRoles.PlatformAdmin)]
[AutoValidateAntiforgeryToken]
public sealed class AdminController(
    AppDbContext dbContext,
    UserManager<User> userManager) : Controller
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        var pendingReportCount = await dbContext.ListingReports
            .AsNoTracking()
            .CountAsync(report => report.Status == TryOutSpotListingReportStatuses.Pending, cancellationToken);
        var inReviewReportCount = await dbContext.ListingReports
            .AsNoTracking()
            .CountAsync(report => report.Status == TryOutSpotListingReportStatuses.InReview, cancellationToken);
        var activeUserCount = await dbContext.Users
            .AsNoTracking()
            .CountAsync(user => user.IsActive, cancellationToken);
        var activePlayerListingCount = await dbContext.PlayerListings
            .AsNoTracking()
            .CountAsync(listing => listing.IsActive, cancellationToken);
        var activeTeamOpportunityCount = await dbContext.Opportunities
            .AsNoTracking()
            .CountAsync(opportunity => opportunity.IsActive, cancellationToken);

        var recentReports = await BuildReportQuery(null, null, null)
            .OrderBy(report => report.Status == TryOutSpotListingReportStatuses.Pending ? 0
                : report.Status == TryOutSpotListingReportStatuses.InReview ? 1
                : 2)
            .ThenByDescending(report => report.CreatedAt)
            .Take(8)
            .ToArrayAsync(cancellationToken);

        return View(new AdminDashboardPageModel(
            pendingReportCount,
            inReviewReportCount,
            activeUserCount,
            activePlayerListingCount,
            activeTeamOpportunityCount,
            recentReports.Select(ToReportListItem).ToArray()));
    }

    [HttpGet("reports")]
    public async Task<IActionResult> Reports(
        [FromQuery] string? status,
        [FromQuery] string? targetType,
        [FromQuery(Name = "q")] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var normalizedStatus = TryOutSpotListingReportStatuses.Normalize(status);
        var normalizedTargetType = TryOutSpotListingReportTargetTypes.Normalize(targetType);
        var query = BuildReportQuery(normalizedStatus, normalizedTargetType, search);

        var totalCount = await query.CountAsync(cancellationToken);
        var reports = await query
            .OrderBy(report => report.Status == TryOutSpotListingReportStatuses.Pending ? 0
                : report.Status == TryOutSpotListingReportStatuses.InReview ? 1
                : 2)
            .ThenByDescending(report => report.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        return View(new AdminReportListPageModel
        {
            Reports = reports.Select(ToReportListItem).ToArray(),
            Status = normalizedStatus,
            TargetType = normalizedTargetType,
            Search = NormalizeOptional(search),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = CalculateTotalPages(totalCount, pageSize),
            StatusOptions = TryOutSpotListingReportStatuses.Values,
            TargetTypeOptions = TryOutSpotListingReportTargetTypes.Values
        });
    }

    [HttpGet("reports/{reportId:guid}")]
    public async Task<IActionResult> ReportDetail(Guid reportId, CancellationToken cancellationToken = default)
    {
        var report = await LoadReportDetailQuery()
            .AsNoTracking()
            .SingleOrDefaultAsync(currentReport => currentReport.Id == reportId, cancellationToken);
        if (report is null)
        {
            return NotFound();
        }

        return View(await BuildReportDetailPageModelAsync(report, cancellationToken));
    }

    [HttpPost("reports/{reportId:guid}/review")]
    public async Task<IActionResult> ReviewReport(
        Guid reportId,
        AdminReviewReportForm form,
        CancellationToken cancellationToken = default)
    {
        var normalizedStatus = TryOutSpotListingReportStatuses.Normalize(form.Status);
        if (normalizedStatus is null)
        {
            TempData["StatusMessage"] = "Select a supported report status.";
            return RedirectToAction(nameof(ReportDetail), new { reportId });
        }

        var report = await dbContext.ListingReports
            .SingleOrDefaultAsync(currentReport => currentReport.Id == reportId, cancellationToken);
        if (report is null)
        {
            return NotFound();
        }

        if (!TryGetCurrentUserId(out var adminUserId))
        {
            return Unauthorized();
        }

        var now = DateTime.UtcNow;
        report.Status = normalizedStatus;
        report.AdminNotes = NormalizeOptional(form.AdminNotes);
        report.ReviewedByUserId = adminUserId;
        report.ReviewedAt = now;
        report.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = "Report review updated.";
        return RedirectToAction(nameof(ReportDetail), new { reportId });
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users(
        [FromQuery(Name = "q")] string? search,
        [FromQuery] string? accountType,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var query = dbContext.Users.AsNoTracking();

        var normalizedSearch = NormalizeOptional(search);
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var loweredSearch = normalizedSearch.ToLowerInvariant();
            query = query.Where(user =>
                user.Email!.ToLower().Contains(loweredSearch)
                || user.FirstName.ToLower().Contains(loweredSearch)
                || user.LastName.ToLower().Contains(loweredSearch));
        }

        if (isActive.HasValue)
        {
            query = query.Where(user => user.IsActive == isActive.Value);
        }

        var normalizedAccountType = TryOutSpotRoles.NormalizeRole(accountType);
        if (normalizedAccountType is not null)
        {
            query = await ApplyRoleFilterAsync(query, normalizedAccountType, cancellationToken);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var users = await query
            .OrderByDescending(user => user.CreatedAt)
            .ThenBy(user => user.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new UserListProjection(
                user.Id,
                user.Email ?? string.Empty,
                user.FirstName,
                user.LastName,
                user.IsActive,
                user.EmailConfirmed,
                user.PhoneNumberConfirmed,
                user.UserPlayerRelationships.Count,
                user.PlayerListings.Count,
                user.UserTeamRoles.Count(role => role.IsActive),
                user.CreatedAt,
                user.UpdatedAt))
            .ToArrayAsync(cancellationToken);

        var roleLookup = await GetRoleLookupAsync(users.Select(user => user.UserId), cancellationToken);

        return View(new AdminUserListPageModel
        {
            Users = users
                .Select(user => new AdminUserListItem(
                    user.UserId,
                    user.Email,
                    user.FirstName,
                    user.LastName,
                    GetRolesForUser(roleLookup, user.UserId),
                    user.IsActive,
                    user.EmailConfirmed,
                    user.PhoneNumberConfirmed,
                    user.PlayerProfileCount,
                    user.PlayerListingCount,
                    user.TeamCount,
                    user.CreatedAt,
                    user.UpdatedAt))
                .ToArray(),
            Search = normalizedSearch,
            AccountType = normalizedAccountType,
            IsActive = isActive,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = CalculateTotalPages(totalCount, pageSize),
            AccountTypeOptions = TryOutSpotRoles.AllRoles
        });
    }

    [HttpGet("users/{userId:guid}")]
    public async Task<IActionResult> UserDetail(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(currentUser => currentUser.Id == userId, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        var roles = TryOutSpotRoles.CanonicalizeRoleSet(
            await userManager.GetRolesAsync(user),
            includePlatformAdmin: true);

        var playerRelationships = await dbContext.UserPlayerRelationships
            .AsNoTracking()
            .Where(relationship => relationship.UserId == userId)
            .Include(relationship => relationship.Player)
                .ThenInclude(player => player.PlayerSports)
                    .ThenInclude(playerSport => playerSport.Sport)
            .OrderBy(relationship => relationship.Player.LastName)
            .ThenBy(relationship => relationship.Player.FirstName)
            .ToArrayAsync(cancellationToken);

        var playerListings = await dbContext.PlayerListings
            .AsNoTracking()
            .Where(listing => listing.UserId == userId)
            .Include(listing => listing.User)
            .Include(listing => listing.Player)
            .Include(listing => listing.Sport)
            .Include(listing => listing.ListingReports)
            .OrderByDescending(listing => listing.UpdatedAt)
            .ToArrayAsync(cancellationToken);

        var teamRoles = await dbContext.UserTeamRoles
            .AsNoTracking()
            .Where(teamRole => teamRole.UserId == userId)
            .Include(teamRole => teamRole.Team)
                .ThenInclude(team => team.Organization)
            .Include(teamRole => teamRole.Team)
                .ThenInclude(team => team.TeamSports)
                    .ThenInclude(teamSport => teamSport.Sport)
            .OrderBy(teamRole => teamRole.Team.Name)
            .ToArrayAsync(cancellationToken);

        var teamIds = teamRoles
            .Select(teamRole => teamRole.TeamId)
            .Distinct()
            .ToArray();
        var teamOpportunities = teamIds.Length == 0
            ? Array.Empty<Opportunity>()
            : await dbContext.Opportunities
                .AsNoTracking()
                .Where(opportunity => teamIds.Contains(opportunity.TeamId))
                .Include(opportunity => opportunity.Team)
                    .ThenInclude(team => team.Organization)
                .Include(opportunity => opportunity.Sport)
                .Include(opportunity => opportunity.ListingReports)
                .OrderByDescending(opportunity => opportunity.UpdatedAt)
                .ToArrayAsync(cancellationToken);

        return View(new AdminUserDetailPageModel
        {
            User = new AdminUserListItem(
                user.Id,
                user.Email ?? string.Empty,
                user.FirstName,
                user.LastName,
                roles,
                user.IsActive,
                user.EmailConfirmed,
                user.PhoneNumberConfirmed,
                playerRelationships.Length,
                playerListings.Length,
                teamRoles.Count(teamRole => teamRole.IsActive),
                user.CreatedAt,
                user.UpdatedAt),
            PhoneNumber = user.PhoneNumber,
            DateOfBirth = user.DateOfBirth,
            City = user.City,
            State = user.State,
            ZipCode = user.ZipCode,
            PlayerProfiles = playerRelationships.Select(ToPlayerProfileItem).ToArray(),
            PlayerListings = playerListings.Select(ToPlayerListingItem).ToArray(),
            Teams = teamRoles.Select(ToTeamProfileItem).ToArray(),
            TeamOpportunities = teamOpportunities.Select(ToTeamOpportunityItem).ToArray()
        });
    }

    [HttpGet("player-listings")]
    public async Task<IActionResult> PlayerListings(
        [FromQuery(Name = "q")] string? search,
        [FromQuery] bool? isPublished,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var query = dbContext.PlayerListings.AsNoTracking();

        if (isPublished.HasValue)
        {
            query = query.Where(listing => listing.IsPublished == isPublished.Value);
        }

        if (isActive.HasValue)
        {
            query = query.Where(listing => listing.IsActive == isActive.Value);
        }

        var normalizedSearch = NormalizeOptional(search);
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var loweredSearch = normalizedSearch.ToLowerInvariant();
            query = query.Where(listing =>
                listing.Title.ToLower().Contains(loweredSearch)
                || (listing.Description != null && listing.Description.ToLower().Contains(loweredSearch))
                || listing.User.Email!.ToLower().Contains(loweredSearch)
                || listing.User.FirstName.ToLower().Contains(loweredSearch)
                || listing.User.LastName.ToLower().Contains(loweredSearch)
                || (listing.Player != null && listing.Player.FirstName.ToLower().Contains(loweredSearch))
                || (listing.Player != null && listing.Player.LastName.ToLower().Contains(loweredSearch)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var listings = await query
            .Include(listing => listing.User)
            .Include(listing => listing.Player)
            .Include(listing => listing.Sport)
            .Include(listing => listing.ListingReports)
            .OrderByDescending(listing => listing.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        return View(new AdminListingListPageModel<AdminPlayerListingListItem>
        {
            Listings = listings.Select(ToPlayerListingItem).ToArray(),
            Search = normalizedSearch,
            IsPublished = isPublished,
            IsActive = isActive,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = CalculateTotalPages(totalCount, pageSize)
        });
    }

    [HttpGet("team-opportunities")]
    public async Task<IActionResult> TeamOpportunities(
        [FromQuery(Name = "q")] string? search,
        [FromQuery] bool? isPublished,
        [FromQuery] bool? isActive,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var query = dbContext.Opportunities.AsNoTracking();

        if (isPublished.HasValue)
        {
            query = query.Where(opportunity => opportunity.IsPublished == isPublished.Value);
        }

        if (isActive.HasValue)
        {
            query = query.Where(opportunity => opportunity.IsActive == isActive.Value);
        }

        var normalizedSearch = NormalizeOptional(search);
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var loweredSearch = normalizedSearch.ToLowerInvariant();
            query = query.Where(opportunity =>
                opportunity.Title.ToLower().Contains(loweredSearch)
                || (opportunity.Description != null && opportunity.Description.ToLower().Contains(loweredSearch))
                || opportunity.Team.Name.ToLower().Contains(loweredSearch)
                || (opportunity.Team.Organization != null && opportunity.Team.Organization.Name.ToLower().Contains(loweredSearch)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var opportunities = await query
            .Include(opportunity => opportunity.Team)
                .ThenInclude(team => team.Organization)
            .Include(opportunity => opportunity.Sport)
            .Include(opportunity => opportunity.ListingReports)
            .OrderByDescending(opportunity => opportunity.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        return View(new AdminListingListPageModel<AdminTeamOpportunityListItem>
        {
            Listings = opportunities.Select(ToTeamOpportunityItem).ToArray(),
            Search = normalizedSearch,
            IsPublished = isPublished,
            IsActive = isActive,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = CalculateTotalPages(totalCount, pageSize)
        });
    }

    private IQueryable<ListingReport> BuildReportQuery(string? status, string? targetType, string? search)
    {
        var query = dbContext.ListingReports
            .AsNoTracking()
            .Include(report => report.ReporterUser)
            .Include(report => report.ReviewedByUser)
            .Include(report => report.PlayerListing)
            .Include(report => report.Opportunity)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(report => report.Status == status);
        }

        if (string.Equals(targetType, TryOutSpotListingReportTargetTypes.PlayerListing, StringComparison.Ordinal))
        {
            query = query.Where(report => report.PlayerListingId != null);
        }
        else if (string.Equals(targetType, TryOutSpotListingReportTargetTypes.TeamOpportunity, StringComparison.Ordinal))
        {
            query = query.Where(report => report.OpportunityId != null);
        }

        var normalizedSearch = NormalizeOptional(search);
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var loweredSearch = normalizedSearch.ToLowerInvariant();
            query = query.Where(report =>
                report.Reason.ToLower().Contains(loweredSearch)
                || (report.Details != null && report.Details.ToLower().Contains(loweredSearch))
                || report.ReporterUser.Email!.ToLower().Contains(loweredSearch)
                || report.ReporterUser.FirstName.ToLower().Contains(loweredSearch)
                || report.ReporterUser.LastName.ToLower().Contains(loweredSearch)
                || (report.PlayerListing != null && report.PlayerListing.Title.ToLower().Contains(loweredSearch))
                || (report.Opportunity != null && report.Opportunity.Title.ToLower().Contains(loweredSearch)));
        }

        return query;
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

    private async Task<AdminReportDetailPageModel> BuildReportDetailPageModelAsync(
        ListingReport report,
        CancellationToken cancellationToken)
    {
        var reportCounts = await GetReportCountsAsync(report.PlayerListingId, report.OpportunityId, cancellationToken);
        return new AdminReportDetailPageModel
        {
            Report = ToReportListItem(report),
            AdminNotes = report.AdminNotes,
            Reporter = ToUserSummaryItem(report.ReporterUser),
            ReviewedBy = report.ReviewedByUser is null ? null : ToUserSummaryItem(report.ReviewedByUser),
            PlayerListing = report.PlayerListing is null
                ? null
                : ToPlayerListingDetailItem(report.PlayerListing, reportCounts.OpenCount, reportCounts.TotalCount),
            TeamOpportunity = report.Opportunity is null
                ? null
                : ToTeamOpportunityDetailItem(report.Opportunity, reportCounts.OpenCount, reportCounts.TotalCount),
            StatusOptions = TryOutSpotListingReportStatuses.Values
        };
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
        var openCount = await query.CountAsync(IsOpenReportExpression(), cancellationToken);
        return (openCount, totalCount);
    }

    private async Task<IQueryable<User>> ApplyRoleFilterAsync(
        IQueryable<User> query,
        string normalizedRole,
        CancellationToken cancellationToken)
    {
        string[] roleNamesToMatch = string.Equals(normalizedRole, TryOutSpotRoles.TeamRepresentative, StringComparison.OrdinalIgnoreCase)
            ? [TryOutSpotRoles.TeamRepresentative, .. TryOutSpotRoles.LegacyTeamBundleRoles]
            : [normalizedRole];

        var normalizedRoleNamesToMatch = roleNamesToMatch
            .Select(role => role.ToUpperInvariant())
            .ToArray();
        var roleIds = await dbContext.Roles
            .AsNoTracking()
            .Where(currentRole => currentRole.Name != null
                && normalizedRoleNamesToMatch.Contains(currentRole.Name.ToUpper()))
            .Select(currentRole => currentRole.Id)
            .ToArrayAsync(cancellationToken);

        var userIdsForRole = dbContext.UserRoles
            .AsNoTracking()
            .Where(userRole => roleIds.Contains(userRole.RoleId))
            .Select(userRole => userRole.UserId);

        return query.Where(user => userIdsForRole.Contains(user.Id));
    }

    private async Task<IReadOnlyDictionary<Guid, string[]>> GetRoleLookupAsync(
        IEnumerable<Guid> userIds,
        CancellationToken cancellationToken)
    {
        var requestedUserIds = userIds.ToArray();
        if (requestedUserIds.Length == 0)
        {
            return new Dictionary<Guid, string[]>();
        }

        var roles = await dbContext.UserRoles
            .AsNoTracking()
            .Where(userRole => requestedUserIds.Contains(userRole.UserId))
            .Join(
                dbContext.Roles.AsNoTracking(),
                userRole => userRole.RoleId,
                role => role.Id,
                (userRole, role) => new { userRole.UserId, role.Name })
            .ToListAsync(cancellationToken);

        return roles
            .GroupBy(role => role.UserId)
            .ToDictionary(
                group => group.Key,
                group => TryOutSpotRoles.CanonicalizeRoleSet(
                    group.Select(role => role.Name ?? string.Empty),
                    includePlatformAdmin: true));
    }

    private static string[] GetRolesForUser(IReadOnlyDictionary<Guid, string[]> roleLookup, Guid userId)
    {
        return roleLookup.TryGetValue(userId, out var roles) ? roles : [];
    }

    private static AdminReportListItem ToReportListItem(ListingReport report)
    {
        var targetType = report.PlayerListingId.HasValue
            ? TryOutSpotListingReportTargetTypes.PlayerListing
            : TryOutSpotListingReportTargetTypes.TeamOpportunity;
        var targetId = report.PlayerListingId ?? report.OpportunityId ?? Guid.Empty;
        var targetTitle = report.PlayerListing?.Title
            ?? report.Opportunity?.Title
            ?? "Listing unavailable";

        return new AdminReportListItem(
            report.Id,
            targetType,
            targetId,
            targetTitle,
            GetDisplayName(report.ReporterUser),
            report.ReporterUser.Email ?? string.Empty,
            report.ReporterUserId,
            report.Reason,
            report.Details,
            report.Status,
            report.CreatedAt,
            report.UpdatedAt,
            report.ReviewedByUser is null ? null : GetDisplayName(report.ReviewedByUser),
            report.ReviewedAt);
    }

    private static AdminUserSummaryItem ToUserSummaryItem(User user)
    {
        return new AdminUserSummaryItem(
            user.Id,
            GetDisplayName(user),
            user.Email ?? string.Empty,
            user.IsActive);
    }

    private static AdminPlayerProfileItem ToPlayerProfileItem(UserPlayerRelationship relationship)
    {
        var player = relationship.Player;
        var sports = player.PlayerSports
            .Where(playerSport => playerSport.IsActive)
            .OrderBy(playerSport => playerSport.Sport.Name)
            .Select(playerSport => playerSport.Sport.Name)
            .ToArray();

        return new AdminPlayerProfileItem(
            player.Id,
            $"{player.FirstName} {player.LastName}".Trim(),
            player.DateOfBirth,
            player.City,
            player.State,
            player.ZipCode,
            relationship.Relationship,
            relationship.CanManage,
            player.IsSearchable,
            player.IsActive,
            sports);
    }

    private static AdminPlayerListingListItem ToPlayerListingItem(PlayerListing listing)
    {
        return new AdminPlayerListingListItem(
            listing.Id,
            listing.UserId,
            GetDisplayName(listing.User),
            listing.User.Email ?? string.Empty,
            listing.ListingType,
            listing.Title,
            listing.Description,
            listing.Player is null ? null : $"{listing.Player.FirstName} {listing.Player.LastName}".Trim(),
            listing.Sport?.Name,
            listing.City,
            listing.State,
            listing.ZipCode,
            listing.IsPublished,
            listing.IsSearchable,
            listing.IsActive,
            CountOpenReports(listing.ListingReports),
            listing.ListingReports.Count,
            listing.CreatedAt,
            listing.UpdatedAt);
    }

    private static AdminPlayerListingDetailItem ToPlayerListingDetailItem(
        PlayerListing listing,
        int openReportCount,
        int totalReportCount)
    {
        return new AdminPlayerListingDetailItem(
            listing.Id,
            listing.UserId,
            GetDisplayName(listing.User),
            listing.User.Email ?? string.Empty,
            listing.ListingType,
            listing.Title,
            listing.Description,
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

    private static AdminTeamProfileItem ToTeamProfileItem(UserTeamRole teamRole)
    {
        var team = teamRole.Team;
        var sports = team.TeamSports
            .Where(teamSport => teamSport.IsActive)
            .OrderBy(teamSport => teamSport.Sport.Name)
            .Select(teamSport => teamSport.Sport.Name)
            .ToArray();

        return new AdminTeamProfileItem(
            team.Id,
            team.Name,
            team.Organization?.Name,
            teamRole.Role,
            team.TeamLevel,
            team.GeographicScope,
            team.City,
            team.State,
            team.ZipCode,
            team.IsSearchable,
            team.IsContactInfoVisible,
            team.IsActive,
            sports);
    }

    private static AdminTeamOpportunityListItem ToTeamOpportunityItem(Opportunity opportunity)
    {
        return new AdminTeamOpportunityListItem(
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
            opportunity.Team.IsSearchable,
            opportunity.IsActive,
            CountOpenReports(opportunity.ListingReports),
            opportunity.ListingReports.Count,
            opportunity.CreatedAt,
            opportunity.UpdatedAt);
    }

    private static AdminTeamOpportunityDetailItem ToTeamOpportunityDetailItem(
        Opportunity opportunity,
        int openReportCount,
        int totalReportCount)
    {
        var representatives = opportunity.Team.UserTeamRoles
            .Where(teamRole => teamRole.IsActive)
            .OrderBy(teamRole => teamRole.User.Email)
            .Select(teamRole => ToUserSummaryItem(teamRole.User))
            .ToArray();

        return new AdminTeamOpportunityDetailItem(
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
            opportunity.Team.IsSearchable,
            opportunity.IsActive,
            openReportCount,
            totalReportCount,
            representatives,
            opportunity.CreatedAt,
            opportunity.UpdatedAt);
    }

    private static int CountOpenReports(IEnumerable<ListingReport> reports)
    {
        return reports.Count(report => report.Status == TryOutSpotListingReportStatuses.Pending
            || report.Status == TryOutSpotListingReportStatuses.InReview);
    }

    private static System.Linq.Expressions.Expression<Func<ListingReport, bool>> IsOpenReportExpression()
    {
        return report => report.Status == TryOutSpotListingReportStatuses.Pending
            || report.Status == TryOutSpotListingReportStatuses.InReview;
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdClaim, out userId);
    }

    private static int CalculateTotalPages(int totalCount, int pageSize)
    {
        return totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string GetDisplayName(User user)
    {
        return $"{user.FirstName} {user.LastName}".Trim();
    }

    private sealed record UserListProjection(
        Guid UserId,
        string Email,
        string FirstName,
        string LastName,
        bool IsActive,
        bool EmailConfirmed,
        bool PhoneNumberConfirmed,
        int PlayerProfileCount,
        int PlayerListingCount,
        int TeamCount,
        DateTime CreatedAt,
        DateTime UpdatedAt);
}
