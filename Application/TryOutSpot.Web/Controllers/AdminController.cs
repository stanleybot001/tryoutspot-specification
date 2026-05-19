using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Listings;
using TryOutSpot.Web.Models.Admin;
using TryOutSpot.Web.Security;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Controllers;

[Route("admin")]
[Authorize(
    AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
    Roles = TryOutSpotRoles.PlatformAdmin)]
[AutoValidateAntiforgeryToken]
public sealed class AdminController(
    AppDbContext dbContext,
    UserManager<User> userManager,
    ILaunchPromotionStatusService launchPromotionStatusService) : Controller
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
        var activeTeamCount = await dbContext.Teams
            .AsNoTracking()
            .CountAsync(team => team.IsActive, cancellationToken);
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
            activeTeamCount,
            activePlayerListingCount,
            activeTeamOpportunityCount,
            recentReports.Select(ToReportListItem).ToArray()));
    }

    [HttpGet("promotions")]
    public async Task<IActionResult> Promotions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var status = await launchPromotionStatusService.GetLaunchFounderOfferStatusAsync(
            pageSize,
            (page - 1) * pageSize,
            cancellationToken);

        return View(new AdminPromotionsPageModel
        {
            PromotionCode = status.PromotionCode,
            ClaimedCount = status.ClaimedCount,
            RemainingCount = status.RemainingCount,
            MaxClaims = status.MaxClaims,
            ActiveGrantCount = status.ActiveGrantCount,
            LatestGrantEndsAtUtc = status.LatestGrantEndsAtUtc,
            Page = page,
            PageSize = pageSize,
            TotalCount = status.ClaimedCount,
            TotalPages = CalculateTotalPages(status.ClaimedCount, pageSize),
            RecentClaims = status.RecentClaims
                .Select(claim => new AdminPromotionClaimItem(
                    claim.UserId,
                    claim.UserDisplayName,
                    claim.UserEmail,
                    claim.GrantedPlanCodes,
                    claim.ActiveGrantCount,
                    claim.LatestGrantEndsAtUtc,
                    claim.RedeemedAtUtc))
                .ToArray()
        });
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
                user.LockoutEnd,
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
                    IsLockedOut(user.LockoutEnd),
                    user.EmailConfirmed,
                    user.PhoneNumberConfirmed,
                    user.PlayerProfileCount,
                    user.PlayerListingCount,
                    user.TeamCount,
                    user.CreatedAt,
                    user.UpdatedAt))
                .ToArray(),
            CurrentAdminUserId = GetCurrentUserIdOrDefault(),
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
        var now = DateTime.UtcNow;
        var complimentaryGrants = await dbContext.ComplimentaryPlanGrants
            .AsNoTracking()
            .Where(grant => grant.UserId == userId)
            .OrderByDescending(grant => grant.RevokedAt == null
                && grant.StartsAt <= now
                && (grant.EndsAt == null || grant.EndsAt > now))
            .ThenByDescending(grant => grant.UpdatedAt)
            .ToArrayAsync(cancellationToken);
        var eligibleComplimentaryPlans = TryOutSpotBillingCatalog.GetEligiblePlanCodesForAccountTypes(roles)
            .Select(TryOutSpotBillingCatalog.GetPlan)
            .Where(plan => plan is not null && plan.RequiresStripeSubscription)
            .Cast<BillingPlanDefinition>()
            .Select(plan => new AdminBillingPlanOption(plan.Code, plan.Name, plan.Audience))
            .ToArray();

        return View(new AdminUserDetailPageModel
        {
            User = new AdminUserListItem(
                user.Id,
                user.Email ?? string.Empty,
                user.FirstName,
                user.LastName,
                roles,
                user.IsActive,
                IsLockedOut(user.LockoutEnd),
                user.EmailConfirmed,
                user.PhoneNumberConfirmed,
                playerRelationships.Length,
                playerListings.Length,
                teamRoles.Count(teamRole => teamRole.IsActive),
                user.CreatedAt,
                user.UpdatedAt),
            CurrentAdminUserId = GetCurrentUserIdOrDefault(),
            PhoneNumber = user.PhoneNumber,
            DateOfBirth = user.DateOfBirth,
            City = user.City,
            State = user.State,
            ZipCode = user.ZipCode,
            PlayerProfiles = playerRelationships.Select(ToPlayerProfileItem).ToArray(),
            PlayerListings = playerListings.Select(ToPlayerListingItem).ToArray(),
            Teams = teamRoles.Select(ToTeamProfileItem).ToArray(),
            TeamOpportunities = teamOpportunities.Select(ToTeamOpportunityItem).ToArray(),
            EligibleComplimentaryGrantPlans = eligibleComplimentaryPlans,
            ComplimentaryGrants = complimentaryGrants.Select(grant => ToComplimentaryGrantItem(grant, now)).ToArray()
        });
    }

    [HttpPost("users/{userId:guid}/complimentary-grants")]
    public async Task<IActionResult> CreateComplimentaryGrant(
        Guid userId,
        AdminCreateComplimentaryGrantForm form,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var adminUserId))
        {
            return Unauthorized();
        }

        var user = await dbContext.Users
            .SingleOrDefaultAsync(currentUser => currentUser.Id == userId && currentUser.IsActive, cancellationToken);
        if (user is null)
        {
            return NotFound();
        }

        var plan = TryOutSpotBillingCatalog.GetPlan(form.PlanCode);
        if (plan is null || !plan.RequiresStripeSubscription)
        {
            TempData["StatusMessage"] = "Choose a paid plan that can be granted.";
            return RedirectToLocalOrAdmin(form.ReturnUrl, nameof(UserDetail), new { userId });
        }

        var roles = await userManager.GetRolesAsync(user);
        if (!TryOutSpotBillingCatalog.IsPlanEligibleForAccountTypes(plan.Code, roles))
        {
            TempData["StatusMessage"] = "That plan is not available for this user's account type.";
            return RedirectToLocalOrAdmin(form.ReturnUrl, nameof(UserDetail), new { userId });
        }

        var startsAt = NormalizeUtc(form.StartsAt) ?? DateTime.UtcNow;
        if (form.DurationMonths is not null && form.EndsAt is not null)
        {
            TempData["StatusMessage"] = "Choose either a month duration or an end date, not both.";
            return RedirectToLocalOrAdmin(form.ReturnUrl, nameof(UserDetail), new { userId });
        }

        if (form.DurationMonths is < 1 or > 120)
        {
            TempData["StatusMessage"] = "Complimentary grant duration must be between 1 and 120 months.";
            return RedirectToLocalOrAdmin(form.ReturnUrl, nameof(UserDetail), new { userId });
        }

        var endsAt = form.DurationMonths is null
            ? NormalizeUtc(form.EndsAt)
            : startsAt.AddMonths(form.DurationMonths.Value);
        if (endsAt is not null && endsAt <= startsAt)
        {
            TempData["StatusMessage"] = "Complimentary grant end date must be after its start date.";
            return RedirectToLocalOrAdmin(form.ReturnUrl, nameof(UserDetail), new { userId });
        }

        var now = DateTime.UtcNow;
        var overlapsExistingGrant = await dbContext.ComplimentaryPlanGrants
            .AsNoTracking()
            .AnyAsync(grant => grant.UserId == userId
                && grant.PlanType == plan.Code
                && grant.ScopeType == TryOutSpotSubscriptionScopeTypes.Account
                && grant.ScopeId == null
                && grant.RevokedAt == null
                && (endsAt == null || grant.StartsAt < endsAt)
                && (grant.EndsAt == null || grant.EndsAt > startsAt),
                cancellationToken);
        if (overlapsExistingGrant)
        {
            TempData["StatusMessage"] = "This user already has an overlapping complimentary grant for that plan.";
            return RedirectToLocalOrAdmin(form.ReturnUrl, nameof(UserDetail), new { userId });
        }

        dbContext.ComplimentaryPlanGrants.Add(new ComplimentaryPlanGrant
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PlanType = plan.Code,
            ScopeType = TryOutSpotSubscriptionScopeTypes.Account,
            StartsAt = startsAt,
            EndsAt = endsAt,
            Source = TryOutSpotPromotionCodes.AdminComplimentaryGrantSource,
            Reason = NormalizeOptional(form.Reason),
            GrantedByUserId = adminUserId,
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = endsAt is null
            ? $"{plan.Name} granted forever."
            : $"{plan.Name} granted through {endsAt.Value.ToLocalTime():MMM d, yyyy}.";
        return RedirectToLocalOrAdmin(form.ReturnUrl, nameof(UserDetail), new { userId });
    }

    [HttpPost("users/{userId:guid}/complimentary-grants/{grantId:guid}/revoke")]
    public async Task<IActionResult> RevokeComplimentaryGrant(
        Guid userId,
        Guid grantId,
        AdminRevokeComplimentaryGrantForm form,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var adminUserId))
        {
            return Unauthorized();
        }

        var grant = await dbContext.ComplimentaryPlanGrants
            .SingleOrDefaultAsync(currentGrant => currentGrant.Id == grantId && currentGrant.UserId == userId, cancellationToken);
        if (grant is null)
        {
            return NotFound();
        }

        if (grant.RevokedAt is null)
        {
            var now = DateTime.UtcNow;
            grant.RevokedAt = now;
            grant.RevokedByUserId = adminUserId;
            grant.RevokeReason = NormalizeOptional(form.Reason);
            grant.UpdatedAt = now;
            await dbContext.SaveChangesAsync(cancellationToken);
            TempData["StatusMessage"] = "Complimentary grant revoked.";
        }
        else
        {
            TempData["StatusMessage"] = "Complimentary grant was already revoked.";
        }

        return RedirectToLocalOrAdmin(form.ReturnUrl, nameof(UserDetail), new { userId });
    }

    [HttpPost("users/{userId:guid}/suspend")]
    public async Task<IActionResult> SuspendUser(
        Guid userId,
        [FromForm] string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        if (IsCurrentAdminUser(userId))
        {
            TempData["StatusMessage"] = "Platform administrators cannot suspend their own account.";
            return RedirectToLocalOrAdmin(returnUrl, nameof(Users));
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return NotFound();
        }

        user.IsActive = false;
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.UtcNow.AddYears(100);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.UpdatedAt = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            TempData["StatusMessage"] = "User could not be suspended.";
            return RedirectToLocalOrAdmin(returnUrl, nameof(UserDetail), new { userId });
        }

        TempData["StatusMessage"] = "User suspended.";
        return RedirectToLocalOrAdmin(returnUrl, nameof(UserDetail), new { userId });
    }

    [HttpPost("users/{userId:guid}/reactivate")]
    public async Task<IActionResult> ReactivateUser(
        Guid userId,
        [FromForm] string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return NotFound();
        }

        user.IsActive = true;
        user.LockoutEnabled = true;
        user.LockoutEnd = null;
        user.AccessFailedCount = 0;
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.UpdatedAt = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            TempData["StatusMessage"] = "User could not be reactivated.";
            return RedirectToLocalOrAdmin(returnUrl, nameof(UserDetail), new { userId });
        }

        TempData["StatusMessage"] = "User reactivated.";
        return RedirectToLocalOrAdmin(returnUrl, nameof(UserDetail), new { userId });
    }

    [HttpGet("teams")]
    public async Task<IActionResult> Teams(
        [FromQuery(Name = "q")] string? search,
        [FromQuery] bool? isActive,
        [FromQuery] bool? isSearchable,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var query = dbContext.Teams.AsNoTracking();

        if (isActive.HasValue)
        {
            query = query.Where(team => team.IsActive == isActive.Value);
        }

        if (isSearchable.HasValue)
        {
            query = query.Where(team => team.IsSearchable == isSearchable.Value);
        }

        var normalizedSearch = NormalizeOptional(search);
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var loweredSearch = normalizedSearch.ToLowerInvariant();
            query = query.Where(team =>
                team.Name.ToLower().Contains(loweredSearch)
                || (team.Organization != null && team.Organization.Name.ToLower().Contains(loweredSearch))
                || (team.City != null && team.City.ToLower().Contains(loweredSearch))
                || (team.State != null && team.State.ToLower().Contains(loweredSearch))
                || (team.ZipCode != null && team.ZipCode.ToLower().Contains(loweredSearch))
                || team.UserTeamRoles.Any(teamRole =>
                    teamRole.User.Email!.ToLower().Contains(loweredSearch)
                    || teamRole.User.FirstName.ToLower().Contains(loweredSearch)
                    || teamRole.User.LastName.ToLower().Contains(loweredSearch)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var teams = await query
            .Include(team => team.Organization)
            .Include(team => team.TeamSports)
                .ThenInclude(teamSport => teamSport.Sport)
            .OrderByDescending(team => team.UpdatedAt)
            .ThenBy(team => team.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        var teamIds = teams.Select(team => team.Id).ToArray();
        var representativeCounts = teamIds.Length == 0
            ? new Dictionary<Guid, int>()
            : await dbContext.UserTeamRoles
                .AsNoTracking()
                .Where(teamRole => teamIds.Contains(teamRole.TeamId) && teamRole.IsActive)
                .GroupBy(teamRole => teamRole.TeamId)
                .Select(group => new { TeamId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(group => group.TeamId, group => group.Count, cancellationToken);
        var opportunityCounts = teamIds.Length == 0
            ? new Dictionary<Guid, TeamOpportunityCountProjection>()
            : await dbContext.Opportunities
                .AsNoTracking()
                .Where(opportunity => teamIds.Contains(opportunity.TeamId))
                .GroupBy(opportunity => opportunity.TeamId)
                .Select(group => new TeamOpportunityCountProjection(
                    group.Key,
                    group.Count(),
                    group.Count(opportunity => opportunity.IsActive)))
                .ToDictionaryAsync(group => group.TeamId, cancellationToken);
        var openReportCounts = teamIds.Length == 0
            ? new Dictionary<Guid, int>()
            : await dbContext.ListingReports
                .AsNoTracking()
                .Where(report => report.Opportunity != null && teamIds.Contains(report.Opportunity.TeamId))
                .Where(IsOpenReportExpression())
                .GroupBy(report => report.Opportunity!.TeamId)
                .Select(group => new { TeamId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(group => group.TeamId, group => group.Count, cancellationToken);

        return View(new AdminTeamListPageModel
        {
            Teams = teams.Select(team => ToTeamListItem(
                    team,
                    representativeCounts.GetValueOrDefault(team.Id),
                    opportunityCounts.GetValueOrDefault(team.Id),
                    openReportCounts.GetValueOrDefault(team.Id)))
                .ToArray(),
            Search = normalizedSearch,
            IsActive = isActive,
            IsSearchable = isSearchable,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = CalculateTotalPages(totalCount, pageSize)
        });
    }

    [HttpPost("teams/{teamId:guid}/suspend")]
    public async Task<IActionResult> SuspendTeam(
        Guid teamId,
        [FromForm] string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var team = await dbContext.Teams
            .SingleOrDefaultAsync(currentTeam => currentTeam.Id == teamId, cancellationToken);
        if (team is null)
        {
            return NotFound();
        }

        team.IsActive = false;
        team.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = "Team suspended.";
        return RedirectToLocalOrAdmin(returnUrl, nameof(Teams));
    }

    [HttpPost("teams/{teamId:guid}/reactivate")]
    public async Task<IActionResult> ReactivateTeam(
        Guid teamId,
        [FromForm] string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var team = await dbContext.Teams
            .SingleOrDefaultAsync(currentTeam => currentTeam.Id == teamId, cancellationToken);
        if (team is null)
        {
            return NotFound();
        }

        team.IsActive = true;
        team.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = "Team reactivated.";
        return RedirectToLocalOrAdmin(returnUrl, nameof(Teams));
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

    [HttpPost("player-listings/{listingId:guid}/deactivate")]
    public async Task<IActionResult> DeactivatePlayerListing(
        Guid listingId,
        [FromForm] string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var listing = await dbContext.PlayerListings
            .SingleOrDefaultAsync(currentListing => currentListing.Id == listingId, cancellationToken);
        if (listing is null)
        {
            return NotFound();
        }

        listing.IsActive = false;
        listing.IsPublished = false;
        listing.PublishedAt = null;
        listing.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = "Player listing deactivated.";
        return RedirectToLocalOrAdmin(returnUrl, nameof(PlayerListings));
    }

    [HttpPost("player-listings/{listingId:guid}/reactivate")]
    public async Task<IActionResult> ReactivatePlayerListing(
        Guid listingId,
        [FromForm] string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var listing = await dbContext.PlayerListings
            .SingleOrDefaultAsync(currentListing => currentListing.Id == listingId, cancellationToken);
        if (listing is null)
        {
            return NotFound();
        }

        listing.IsActive = true;
        listing.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = "Player listing reactivated. It remains unpublished until the owner republishes it.";
        return RedirectToLocalOrAdmin(returnUrl, nameof(PlayerListings));
    }

    [HttpPost("player-listings/{listingId:guid}/delete")]
    public async Task<IActionResult> DeletePlayerListing(
        Guid listingId,
        [FromForm] string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var listing = await dbContext.PlayerListings
            .SingleOrDefaultAsync(currentListing => currentListing.Id == listingId, cancellationToken);
        if (listing is null)
        {
            return NotFound();
        }

        var now = DateTime.UtcNow;
        listing.IsActive = false;
        listing.IsPublished = false;
        listing.IsSearchable = false;
        listing.PublishedAt = null;
        listing.ExpiresAt = now;
        listing.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = "Player listing deleted from public and owner-facing listings. The record is retained for admin audit history.";
        return RedirectToLocalOrAdmin(returnUrl, nameof(PlayerListings));
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

    [HttpPost("team-opportunities/{opportunityId:guid}/deactivate")]
    public async Task<IActionResult> DeactivateTeamOpportunity(
        Guid opportunityId,
        [FromForm] string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var opportunity = await dbContext.Opportunities
            .SingleOrDefaultAsync(currentOpportunity => currentOpportunity.Id == opportunityId, cancellationToken);
        if (opportunity is null)
        {
            return NotFound();
        }

        opportunity.IsActive = false;
        opportunity.IsPublished = false;
        opportunity.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = "Team opportunity deactivated.";
        return RedirectToLocalOrAdmin(returnUrl, nameof(TeamOpportunities));
    }

    [HttpPost("team-opportunities/{opportunityId:guid}/reactivate")]
    public async Task<IActionResult> ReactivateTeamOpportunity(
        Guid opportunityId,
        [FromForm] string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var opportunity = await dbContext.Opportunities
            .SingleOrDefaultAsync(currentOpportunity => currentOpportunity.Id == opportunityId, cancellationToken);
        if (opportunity is null)
        {
            return NotFound();
        }

        opportunity.IsActive = true;
        opportunity.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = "Team opportunity reactivated. It remains unpublished until a team representative republishes it.";
        return RedirectToLocalOrAdmin(returnUrl, nameof(TeamOpportunities));
    }

    [HttpPost("team-opportunities/{opportunityId:guid}/delete")]
    public async Task<IActionResult> DeleteTeamOpportunity(
        Guid opportunityId,
        [FromForm] string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var opportunity = await dbContext.Opportunities
            .SingleOrDefaultAsync(currentOpportunity => currentOpportunity.Id == opportunityId, cancellationToken);
        if (opportunity is null)
        {
            return NotFound();
        }

        var now = DateTime.UtcNow;
        opportunity.IsActive = false;
        opportunity.IsPublished = false;
        opportunity.ListingEndDate = now;
        opportunity.ExpiresAt = now;
        opportunity.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = "Team opportunity deleted from public and owner-facing listings. The record is retained for admin audit history.";
        return RedirectToLocalOrAdmin(returnUrl, nameof(TeamOpportunities));
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

    private static AdminTeamListItem ToTeamListItem(
        Team team,
        int representativeCount,
        TeamOpportunityCountProjection? opportunityCounts,
        int openReportCount)
    {
        var sports = team.TeamSports
            .Where(teamSport => teamSport.IsActive)
            .OrderBy(teamSport => teamSport.Sport.Name)
            .Select(teamSport => teamSport.Sport.Name)
            .ToArray();

        return new AdminTeamListItem(
            team.Id,
            team.Name,
            team.Organization?.Name,
            team.TeamLevel,
            team.GeographicScope,
            team.City,
            team.State,
            team.ZipCode,
            team.IsSearchable,
            team.IsContactInfoVisible,
            team.IsActive,
            representativeCount,
            opportunityCounts?.TotalCount ?? 0,
            opportunityCounts?.ActiveCount ?? 0,
            openReportCount,
            sports,
            team.CreatedAt,
            team.UpdatedAt);
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

    private static AdminComplimentaryGrantItem ToComplimentaryGrantItem(
        ComplimentaryPlanGrant grant,
        DateTime now)
    {
        var planCode = TryOutSpotBillingCatalog.NormalizePlanCode(grant.PlanType) ?? grant.PlanType;
        var plan = TryOutSpotBillingCatalog.GetPlan(planCode);

        return new AdminComplimentaryGrantItem(
            grant.Id,
            planCode,
            plan?.Name ?? grant.PlanType,
            ComplimentaryPlanGrantMapper.GetStatus(grant, now),
            plan is not null && ComplimentaryPlanGrantMapper.IsActive(grant, now),
            grant.ScopeType,
            grant.ScopeId,
            grant.StartsAt,
            grant.EndsAt,
            grant.Source,
            grant.PromotionCode,
            grant.Reason,
            grant.GrantedByUserId,
            grant.CreatedAt,
            grant.UpdatedAt,
            grant.RevokedAt,
            grant.RevokedByUserId,
            grant.RevokeReason);
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

    private Guid GetCurrentUserIdOrDefault()
    {
        return TryGetCurrentUserId(out var userId) ? userId : Guid.Empty;
    }

    private bool IsCurrentAdminUser(Guid userId)
    {
        return TryGetCurrentUserId(out var currentUserId) && currentUserId == userId;
    }

    private IActionResult RedirectToLocalOrAdmin(string? returnUrl, string fallbackAction, object? routeValues = null)
    {
        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return RedirectToAction(fallbackAction, routeValues);
    }

    private static bool IsLockedOut(DateTimeOffset? lockoutEnd)
    {
        return lockoutEnd.HasValue && lockoutEnd.Value > DateTimeOffset.UtcNow;
    }

    private static int CalculateTotalPages(int totalCount, int pageSize)
    {
        return totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
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
        DateTimeOffset? LockoutEnd,
        bool EmailConfirmed,
        bool PhoneNumberConfirmed,
        int PlayerProfileCount,
        int PlayerListingCount,
        int TeamCount,
        DateTime CreatedAt,
        DateTime UpdatedAt);

    private sealed record TeamOpportunityCountProjection(
        Guid TeamId,
        int TotalCount,
        int ActiveCount);
}
