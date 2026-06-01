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
    ILaunchPromotionStatusService launchPromotionStatusService,
    IFlyerImportService flyerImportService,
    IFlyerAiExtractionService flyerAiExtractionService,
    IFlyerImportRemoteFileFetcher flyerImportRemoteFileFetcher,
    IPdfStorageService pdfStorageService) : Controller
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;
    private static readonly string[] OpportunityTypeOptions =
    [
        "tryout",
        "roster_opening",
        "pickup_player",
        "camp",
        "clinic",
        "tournament",
        "private_workout",
        "other"
    ];

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        var pendingReportCount = await dbContext.ListingReports
            .AsNoTracking()
            .CountAsync(report => report.Status == TryOutSpotListingReportStatuses.Pending, cancellationToken);
        var inReviewReportCount = await dbContext.ListingReports
            .AsNoTracking()
            .CountAsync(report => report.Status == TryOutSpotListingReportStatuses.InReview, cancellationToken);
        var pendingFlyerImportCount = await dbContext.FlyerImports
            .AsNoTracking()
            .CountAsync(flyerImport => flyerImport.Status == TryOutSpotFlyerImportStatuses.PendingReview, cancellationToken);
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
            pendingFlyerImportCount,
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
            PromotionName = status.PromotionName,
            IsEnabled = status.IsEnabled,
            ClaimedCount = status.ClaimedCount,
            RemainingCount = status.RemainingCount,
            MaxClaims = status.MaxClaims,
            GrantMonths = status.GrantMonths,
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

    [HttpPost("promotions/settings")]
    public async Task<IActionResult> UpdatePromotionSettings(
        AdminPromotionSettingsForm form,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var adminUserId))
        {
            return Unauthorized();
        }

        if (!ValidatePromotionSettingsForm(form))
        {
            return RedirectToAction(nameof(Promotions));
        }

        await launchPromotionStatusService.UpdateLaunchFounderOfferSettingsAsync(
            form.Name,
            form.MaxRedemptions,
            form.GrantMonths,
            form.IsEnabled,
            adminUserId,
            cancellationToken);

        TempData["StatusMessage"] = "Promotion settings updated. Existing grants were left unchanged.";
        return RedirectToAction(nameof(Promotions));
    }

    [HttpPost("promotions/reset")]
    public async Task<IActionResult> ResetPromotion(
        AdminPromotionSettingsForm form,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var adminUserId))
        {
            return Unauthorized();
        }

        if (!ValidatePromotionSettingsForm(form))
        {
            return RedirectToAction(nameof(Promotions));
        }

        await launchPromotionStatusService.ResetLaunchFounderOfferAsync(
            form.Name,
            form.MaxRedemptions,
            form.GrantMonths,
            form.IsEnabled,
            adminUserId,
            cancellationToken);

        TempData["StatusMessage"] = "Promotion reset. The claim counter now starts at zero for the new campaign code.";
        return RedirectToAction(nameof(Promotions));
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
        var teamOpportunityIds = teamOpportunities
            .Select(opportunity => opportunity.Id)
            .ToArray();
        Guid[] flyerTeamOpportunityIds = teamOpportunityIds.Length == 0
            ? []
            : await dbContext.FlyerImports
                .AsNoTracking()
                .Where(flyerImport => flyerImport.OpportunityId.HasValue
                    && teamOpportunityIds.Contains(flyerImport.OpportunityId.Value))
                .Select(flyerImport => flyerImport.OpportunityId!.Value)
                .Distinct()
                .ToArrayAsync(cancellationToken);
        var flyerTeamOpportunityIdSet = flyerTeamOpportunityIds.ToHashSet();
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
            TeamOpportunities = teamOpportunities
                .Select(opportunity => ToTeamOpportunityItem(
                    opportunity,
                    flyerTeamOpportunityIdSet.Contains(opportunity.Id)))
                .ToArray(),
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

    [HttpGet("teams/{teamId:guid}/edit")]
    public async Task<IActionResult> EditTeam(
        Guid teamId,
        [FromQuery] string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var team = await dbContext.Teams
            .AsNoTracking()
            .SingleOrDefaultAsync(currentTeam => currentTeam.Id == teamId, cancellationToken);
        if (team is null)
        {
            return NotFound();
        }

        return View("EditTeam", BuildTeamEditPageModel(team, returnUrl));
    }

    [HttpPost("teams/{teamId:guid}/edit")]
    public async Task<IActionResult> EditTeam(
        Guid teamId,
        [Bind(Prefix = "Form")] AdminTeamEditForm form,
        [FromForm] string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var team = await dbContext.Teams
            .SingleOrDefaultAsync(currentTeam => currentTeam.Id == teamId, cancellationToken);
        if (team is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View("EditTeam", new AdminTeamEditPageModel
            {
                TeamId = teamId,
                ReturnUrl = returnUrl,
                Form = form
            });
        }

        var now = DateTime.UtcNow;
        team.Name = NormalizeLength(form.Name, 200) ?? team.Name;
        team.TeamLevel = NormalizeLength(form.TeamLevel, 50);
        team.GeographicScope = NormalizeLength(form.GeographicScope, 50) ?? "Local";
        team.Description = NormalizeLength(form.Description, 2000);
        team.WebsiteUrl = NormalizeLength(form.WebsiteUrl, 500);
        team.Address = NormalizeLength(form.Address, 500);
        team.City = NormalizeLength(form.City, 100);
        team.State = NormalizeState(form.State);
        team.ZipCode = NormalizeLength(form.ZipCode, 10);
        team.PhoneNumber = NormalizeLength(form.PhoneNumber, 20);
        team.Email = NormalizeLength(form.Email, 255);
        team.IsSearchable = form.IsSearchable;
        team.IsContactInfoVisible = form.IsContactInfoVisible;
        team.IsActive = form.IsActive;
        team.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = "Team profile updated.";
        return RedirectToLocalOrAdmin(returnUrl, nameof(Teams), new { q = team.Name });
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
        var opportunityIds = opportunities
            .Select(opportunity => opportunity.Id)
            .ToArray();
        Guid[] flyerCreatedOpportunityIds = opportunityIds.Length == 0
            ? []
            : await dbContext.FlyerImports
                .AsNoTracking()
                .Where(flyerImport => flyerImport.OpportunityId.HasValue
                    && opportunityIds.Contains(flyerImport.OpportunityId.Value))
                .Select(flyerImport => flyerImport.OpportunityId!.Value)
                .Distinct()
                .ToArrayAsync(cancellationToken);
        var flyerCreatedOpportunityIdSet = flyerCreatedOpportunityIds.ToHashSet();

        return View(new AdminListingListPageModel<AdminTeamOpportunityListItem>
        {
            Listings = opportunities
                .Select(opportunity => ToTeamOpportunityItem(
                    opportunity,
                    flyerCreatedOpportunityIdSet.Contains(opportunity.Id)))
                .ToArray(),
            Search = normalizedSearch,
            IsPublished = isPublished,
            IsActive = isActive,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = CalculateTotalPages(totalCount, pageSize)
        });
    }

    [HttpGet("team-opportunities/{opportunityId:guid}/edit")]
    public async Task<IActionResult> EditTeamOpportunity(
        Guid opportunityId,
        [FromQuery] string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var opportunity = await dbContext.Opportunities
            .AsNoTracking()
            .Include(currentOpportunity => currentOpportunity.Team)
            .SingleOrDefaultAsync(currentOpportunity => currentOpportunity.Id == opportunityId, cancellationToken);
        if (opportunity is null)
        {
            return NotFound();
        }

        return View("EditTeamOpportunity", await BuildTeamOpportunityEditPageModelAsync(
            opportunity,
            BuildTeamOpportunityEditForm(opportunity),
            returnUrl,
            cancellationToken));
    }

    [HttpPost("team-opportunities/{opportunityId:guid}/edit")]
    public async Task<IActionResult> EditTeamOpportunity(
        Guid opportunityId,
        [Bind(Prefix = "Form")] AdminTeamOpportunityEditForm form,
        [FromForm] string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var opportunity = await dbContext.Opportunities
            .Include(currentOpportunity => currentOpportunity.Team)
            .SingleOrDefaultAsync(currentOpportunity => currentOpportunity.Id == opportunityId, cancellationToken);
        if (opportunity is null)
        {
            return NotFound();
        }

        await ValidateTeamOpportunityEditFormAsync(opportunity, form, cancellationToken);
        if (!ModelState.IsValid)
        {
            return View("EditTeamOpportunity", await BuildTeamOpportunityEditPageModelAsync(
                opportunity,
                form,
                returnUrl,
                cancellationToken));
        }

        var now = DateTime.UtcNow;
        var normalizedEventDate = NormalizeUtc(form.EventDate);
        var normalizedEventEndDate = NormalizeUtc(form.EventEndDate);
        var normalizedListingStartDate = NormalizeUtc(form.ListingStartDate);
        var normalizedListingEndDate = NormalizeUtc(form.ListingEndDate);
        var normalizedType = NormalizeOpportunityType(form.Type);
        var shouldPublish = form.IsPublished && form.IsActive;

        opportunity.Team.Name = NormalizeLength(form.TeamName, 200) ?? opportunity.Team.Name;
        opportunity.Team.TeamLevel = NormalizeLength(form.TeamLevel, 50);
        opportunity.Team.Address = NormalizeLength(form.TeamAddress, 500);
        opportunity.Team.City = NormalizeLength(form.TeamCity, 100);
        opportunity.Team.State = NormalizeState(form.TeamState);
        opportunity.Team.ZipCode = NormalizeLength(form.TeamZipCode, 10);
        opportunity.Team.UpdatedAt = now;

        opportunity.Title = NormalizeLength(form.Title, 300) ?? opportunity.Title;
        opportunity.Type = normalizedType;
        opportunity.SportId = form.SportId;
        opportunity.AgeGroup = NormalizeLength(form.AgeGroup, 50);
        opportunity.CompetitionLevel = NormalizeLength(form.CompetitionLevel, 100);
        opportunity.Description = NormalizeLength(form.Description, 4000);
        opportunity.EventDate = normalizedEventDate;
        opportunity.EventEndDate = normalizedEventEndDate;
        opportunity.ListingStartDate = normalizedListingStartDate ?? (shouldPublish ? opportunity.ListingStartDate ?? now : null);
        opportunity.ListingEndDate = normalizedListingEndDate;
        opportunity.ExpiresAt = normalizedListingEndDate ?? ResolveOpportunityExpiration(normalizedEventDate, normalizedEventEndDate);
        opportunity.Location = NormalizeLength(form.Location, 500);
        opportunity.Address = NormalizeLength(form.Address, 500);
        opportunity.City = NormalizeLength(form.City, 100);
        opportunity.State = NormalizeState(form.State);
        opportunity.ZipCode = NormalizeLength(form.ZipCode, 10);
        opportunity.ContactEmail = NormalizeLength(form.ContactEmail, 255);
        opportunity.ContactPhone = NormalizeLength(form.ContactPhone, 20);
        opportunity.WebsiteUrl = NormalizeLength(form.WebsiteUrl, 500);
        opportunity.RequiredEquipment = NormalizeLength(form.RequiredEquipment, 1000);
        opportunity.WhatToBring = NormalizeLength(form.WhatToBring, 1000);
        opportunity.SpecialInstructions = NormalizeLength(form.SpecialInstructions, 2000);
        opportunity.IsActive = form.IsActive;
        opportunity.IsPublished = shouldPublish;
        opportunity.PublishedAt = shouldPublish ? opportunity.PublishedAt ?? now : null;
        opportunity.UpdatedAt = now;

        if (shouldPublish)
        {
            opportunity.Team.IsActive = true;
            opportunity.Team.IsSearchable = true;
        }

        await SyncFlyerImportsForOpportunityAsync(opportunity, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = shouldPublish
            ? "Team opportunity updated and published."
            : "Team opportunity updated.";
        return RedirectToLocalOrAdmin(returnUrl, nameof(TeamOpportunities), new { q = opportunity.Title });
    }

    [HttpGet("team-opportunities/{opportunityId:guid}/flyer")]
    public async Task<IActionResult> TeamOpportunityFlyer(
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        var flyer = await ResolveTeamOpportunityFlyerDownloadAsync(opportunityId, cancellationToken);
        if (flyer is null)
        {
            return NotFound();
        }

        var payload = await pdfStorageService.DownloadFileAsync(flyer.ObjectKey, cancellationToken);
        if (payload is null || payload.Content.Length == 0)
        {
            return NotFound();
        }

        var contentType = ResolveFlyerImportContentType(
            payload.ContentType,
            flyer.ContentType,
            flyer.FileName ?? flyer.ObjectKey);

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(payload.Content, contentType, enableRangeProcessing: false);
    }

    [HttpPost("team-opportunities/{opportunityId:guid}/publish")]
    public async Task<IActionResult> PublishTeamOpportunity(
        Guid opportunityId,
        [FromForm] string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var opportunity = await dbContext.Opportunities
            .Include(currentOpportunity => currentOpportunity.Team)
            .SingleOrDefaultAsync(currentOpportunity => currentOpportunity.Id == opportunityId, cancellationToken);
        if (opportunity is null)
        {
            return NotFound();
        }

        var now = DateTime.UtcNow;
        if (!opportunity.IsPublished
            && !await IsFlyerCreatedOpportunityAsync(opportunity.Id, cancellationToken))
        {
            TempData["StatusMessage"] = "Team-created opportunities must be published from the team account so posting rules are applied. Admin publishing is only for flyer-created opportunities.";
            return RedirectToLocalOrAdmin(returnUrl, nameof(TeamOpportunities));
        }

        if (opportunity.EventDate is null)
        {
            TempData["StatusMessage"] = "Add an event date before publishing this team opportunity.";
            return RedirectToAction(nameof(EditTeamOpportunity), new { opportunityId, returnUrl });
        }

        if (string.IsNullOrWhiteSpace(opportunity.ZipCode))
        {
            TempData["StatusMessage"] = "Add the listing ZIP code before publishing this team opportunity.";
            return RedirectToAction(nameof(EditTeamOpportunity), new { opportunityId, returnUrl });
        }

        var effectiveEndDate = opportunity.ListingEndDate ?? opportunity.ExpiresAt;
        if (effectiveEndDate.HasValue && effectiveEndDate.Value <= now)
        {
            TempData["StatusMessage"] = "This team opportunity has already expired. Update the event or listing dates before publishing.";
            return RedirectToAction(nameof(EditTeamOpportunity), new { opportunityId, returnUrl });
        }

        opportunity.IsActive = true;
        opportunity.IsPublished = true;
        opportunity.PublishedAt ??= now;
        opportunity.ListingStartDate ??= now;
        opportunity.UpdatedAt = now;

        if (!opportunity.Team.IsActive || !opportunity.Team.IsSearchable)
        {
            opportunity.Team.IsActive = true;
            opportunity.Team.IsSearchable = true;
            opportunity.Team.UpdatedAt = now;
        }

        var flyerImports = await dbContext.FlyerImports
            .Where(flyerImport => flyerImport.OpportunityId == opportunity.Id)
            .ToArrayAsync(cancellationToken);
        foreach (var flyerImport in flyerImports)
        {
            flyerImport.Status = TryOutSpotFlyerImportStatuses.Published;
            flyerImport.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = "Team opportunity published.";
        return RedirectToLocalOrAdmin(returnUrl, nameof(TeamOpportunities));
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

    [HttpGet("flyer-imports")]
    public async Task<IActionResult> FlyerImports(
        [FromQuery(Name = "q")] string? search,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var normalizedStatus = TryOutSpotFlyerImportStatuses.Normalize(status);
        var query = BuildFlyerImportQuery(search, normalizedStatus);

        var totalCount = await query.CountAsync(cancellationToken);
        var imports = await query
            .OrderBy(flyerImport => flyerImport.Status == TryOutSpotFlyerImportStatuses.PendingReview ? 0 : 1)
            .ThenByDescending(flyerImport => flyerImport.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        return View(new AdminFlyerImportListPageModel
        {
            Imports = imports.Select(ToFlyerImportListItem).ToArray(),
            Search = NormalizeOptional(search),
            Status = normalizedStatus,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = CalculateTotalPages(totalCount, pageSize),
            StatusOptions = TryOutSpotFlyerImportStatuses.Values
        });
    }

    [HttpGet("flyer-imports/new")]
    public async Task<IActionResult> NewFlyerImport(CancellationToken cancellationToken = default)
    {
        return View("FlyerImportDetail", new AdminFlyerImportDetailPageModel
        {
            Import = new AdminFlyerImportDetailItem(
                Guid.Empty,
                TryOutSpotFlyerImportStatuses.PendingReview,
                "facebook",
                null,
                null,
                false,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                DateTime.UtcNow,
                DateTime.UtcNow),
            Form = new AdminFlyerImportForm
            {
                SourcePlatform = "facebook",
                OpportunityType = "tryout"
            },
            SportOptions = await GetSportOptionsAsync(cancellationToken)
        });
    }

    [HttpPost("flyer-imports")]
    public async Task<IActionResult> CreateFlyerImport(
        [Bind(Prefix = "Form")] AdminFlyerImportForm form,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var adminUserId))
        {
            return Unauthorized();
        }

        UploadedFlyerImportFile? flyerFile = null;
        var upload = await FlyerImportUploadHelper.ParseAsync(form.FlyerFile, cancellationToken);
        if (!upload.Succeeded)
        {
            AddModelErrors(upload.Errors);
        }
        else if (upload.HasFile && upload.File is not null)
        {
            flyerFile = upload.File;
        }
        else if (!string.IsNullOrWhiteSpace(form.OriginalExternalImageUrl))
        {
            var remoteFile = await flyerImportRemoteFileFetcher.FetchAsync(
                form.OriginalExternalImageUrl,
                cancellationToken);
            if (!remoteFile.Succeeded || remoteFile.File is null)
            {
                AddModelErrors(remoteFile.Errors);
            }
            else
            {
                flyerFile = remoteFile.File;
            }
        }
        else
        {
            ModelState.AddModelError(
                nameof(AdminFlyerImportForm.FlyerFile),
                "Upload a flyer image or paste a public image URL for AI extraction.");
        }

        if (!ModelState.IsValid)
        {
            return View("FlyerImportDetail", new AdminFlyerImportDetailPageModel
            {
                Import = BuildNewFlyerImportDetailItem(),
                Form = form,
                SportOptions = await GetSportOptionsAsync(cancellationToken)
            });
        }

        var extraction = await flyerAiExtractionService.ExtractAsync(
            flyerFile!,
            form.SourceUrl,
            form.OriginalExternalImageUrl,
            cancellationToken);
        if (!extraction.Succeeded || extraction.Input is null)
        {
            AddModelErrors(extraction.Errors);
            return View("FlyerImportDetail", new AdminFlyerImportDetailPageModel
            {
                Import = BuildNewFlyerImportDetailItem(),
                Form = form,
                SportOptions = await GetSportOptionsAsync(cancellationToken)
            });
        }

        var extractedInput = MergeFlyerExtractionInput(form, extraction.Input, extraction.ExtractedJson, extraction.ConfidenceJson);
        var result = await flyerImportService.CreateAsync(
            extractedInput,
            flyerFile,
            adminUserId,
            cancellationToken);
        if (!result.Succeeded || result.FlyerImport is null)
        {
            AddModelErrors(result.Errors);
            return View("FlyerImportDetail", new AdminFlyerImportDetailPageModel
            {
                Import = BuildNewFlyerImportDetailItem(),
                Form = form,
                SportOptions = await GetSportOptionsAsync(cancellationToken)
            });
        }

        TempData["StatusMessage"] = "AI read the flyer. Review the extracted fields before creating the listing.";
        return RedirectToAction(nameof(FlyerImportDetail), new { flyerImportId = result.FlyerImport.Id });
    }

    [HttpGet("flyer-imports/{flyerImportId:guid}")]
    public async Task<IActionResult> FlyerImportDetail(
        Guid flyerImportId,
        CancellationToken cancellationToken = default)
    {
        var flyerImport = await LoadFlyerImportDetailQuery()
            .AsNoTracking()
            .SingleOrDefaultAsync(currentImport => currentImport.Id == flyerImportId, cancellationToken);
        if (flyerImport is null)
        {
            return NotFound();
        }

        return View(await BuildFlyerImportDetailPageModelAsync(flyerImport, cancellationToken));
    }

    [HttpGet("flyer-imports/{flyerImportId:guid}/file")]
    public async Task<IActionResult> FlyerImportFile(
        Guid flyerImportId,
        CancellationToken cancellationToken = default)
    {
        var flyerImport = await dbContext.FlyerImports
            .AsNoTracking()
            .Where(currentImport => currentImport.Id == flyerImportId)
            .Select(currentImport => new
            {
                currentImport.StoredObjectKey,
                currentImport.StoredFileName,
                currentImport.StoredContentType
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (flyerImport is null || string.IsNullOrWhiteSpace(flyerImport.StoredObjectKey))
        {
            return NotFound();
        }

        var payload = await flyerImportService.DownloadFlyerAsync(flyerImportId, cancellationToken);
        if (payload is null || payload.Content.Length == 0)
        {
            return NotFound();
        }

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(payload.Content, ResolveFlyerImportContentType(
            payload.ContentType,
            flyerImport.StoredContentType,
            flyerImport.StoredFileName));
    }

    [HttpPost("flyer-imports/{flyerImportId:guid}/update")]
    public async Task<IActionResult> UpdateFlyerImport(
        Guid flyerImportId,
        [Bind(Prefix = "Form")] AdminFlyerImportForm form,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var adminUserId))
        {
            return Unauthorized();
        }

        var existingImport = await LoadFlyerImportDetailQuery()
            .AsNoTracking()
            .SingleOrDefaultAsync(currentImport => currentImport.Id == flyerImportId, cancellationToken);
        if (existingImport is null)
        {
            return NotFound();
        }

        var upload = await FlyerImportUploadHelper.ParseAsync(form.FlyerFile, cancellationToken);
        if (!upload.Succeeded)
        {
            AddModelErrors(upload.Errors);
        }

        if (!ModelState.IsValid)
        {
            return View("FlyerImportDetail", new AdminFlyerImportDetailPageModel
            {
                Import = ToFlyerImportDetailItem(existingImport),
                Form = form,
                SportOptions = await GetSportOptionsAsync(cancellationToken)
            });
        }

        var result = await flyerImportService.UpdateAsync(
            flyerImportId,
            ToFlyerImportInput(form),
            upload.File,
            adminUserId,
            cancellationToken);
        if (!result.Succeeded)
        {
            AddModelErrors(result.Errors);
            return View("FlyerImportDetail", new AdminFlyerImportDetailPageModel
            {
                Import = ToFlyerImportDetailItem(existingImport),
                Form = form,
                SportOptions = await GetSportOptionsAsync(cancellationToken)
            });
        }

        TempData["StatusMessage"] = "Flyer import updated.";
        return RedirectToAction(nameof(FlyerImportDetail), new { flyerImportId });
    }

    [HttpPost("flyer-imports/{flyerImportId:guid}/create-listing")]
    public async Task<IActionResult> CreateListingFromFlyerImport(
        Guid flyerImportId,
        AdminCreateListingFromFlyerImportForm form,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var adminUserId))
        {
            return Unauthorized();
        }

        var result = await flyerImportService.CreateListingAsync(
            flyerImportId,
            adminUserId,
            form.PublishImmediately,
            cancellationToken);
        if (!result.Succeeded)
        {
            TempData["StatusMessage"] = string.Join(" ", result.Errors);
            return RedirectToAction(nameof(FlyerImportDetail), new { flyerImportId });
        }

        TempData["StatusMessage"] = form.PublishImmediately
            ? "Listing created and published from flyer."
            : "Draft listing created from flyer.";
        return RedirectToAction(nameof(FlyerImportDetail), new { flyerImportId });
    }

    [HttpPost("flyer-imports/{flyerImportId:guid}/reject")]
    public async Task<IActionResult> RejectFlyerImport(
        Guid flyerImportId,
        AdminRejectFlyerImportForm form,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var adminUserId))
        {
            return Unauthorized();
        }

        var result = await flyerImportService.RejectAsync(
            flyerImportId,
            adminUserId,
            form.AdminNotes,
            cancellationToken);
        if (!result.Succeeded)
        {
            TempData["StatusMessage"] = string.Join(" ", result.Errors);
            return RedirectToAction(nameof(FlyerImportDetail), new { flyerImportId });
        }

        TempData["StatusMessage"] = "Flyer import rejected.";
        return RedirectToAction(nameof(FlyerImportDetail), new { flyerImportId });
    }

    private IQueryable<FlyerImport> BuildFlyerImportQuery(string? search, string? status)
    {
        var query = dbContext.FlyerImports
            .AsNoTracking()
            .Include(flyerImport => flyerImport.Sport)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(flyerImport => flyerImport.Status == status);
        }

        var normalizedSearch = NormalizeOptional(search);
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var loweredSearch = normalizedSearch.ToLowerInvariant();
            query = query.Where(flyerImport =>
                (flyerImport.Title != null && flyerImport.Title.ToLower().Contains(loweredSearch))
                || (flyerImport.TeamName != null && flyerImport.TeamName.ToLower().Contains(loweredSearch))
                || (flyerImport.OrganizationName != null && flyerImport.OrganizationName.ToLower().Contains(loweredSearch))
                || (flyerImport.City != null && flyerImport.City.ToLower().Contains(loweredSearch))
                || (flyerImport.State != null && flyerImport.State.ToLower().Contains(loweredSearch))
                || (flyerImport.ZipCode != null && flyerImport.ZipCode.ToLower().Contains(loweredSearch))
                || (flyerImport.SourceUrl != null && flyerImport.SourceUrl.ToLower().Contains(loweredSearch))
                || (flyerImport.OriginalExternalImageUrl != null && flyerImport.OriginalExternalImageUrl.ToLower().Contains(loweredSearch)));
        }

        return query;
    }

    private IQueryable<FlyerImport> LoadFlyerImportDetailQuery()
    {
        return dbContext.FlyerImports
            .Include(flyerImport => flyerImport.Sport)
            .Include(flyerImport => flyerImport.ReviewedByUser)
            .Include(flyerImport => flyerImport.Team)
            .Include(flyerImport => flyerImport.Opportunity);
    }

    private async Task<AdminFlyerImportDetailPageModel> BuildFlyerImportDetailPageModelAsync(
        FlyerImport flyerImport,
        CancellationToken cancellationToken)
    {
        return new AdminFlyerImportDetailPageModel
        {
            Import = ToFlyerImportDetailItem(flyerImport),
            Form = ToFlyerImportForm(flyerImport),
            SportOptions = await GetSportOptionsAsync(cancellationToken)
        };
    }

    private async Task<IReadOnlyCollection<AdminSportOption>> GetSportOptionsAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Sports
            .AsNoTracking()
            .Where(sport => sport.IsActive)
            .OrderBy(sport => sport.Name)
            .Select(sport => new AdminSportOption(sport.Id, sport.Name))
            .ToArrayAsync(cancellationToken);
    }

    private static AdminFlyerImportListItem ToFlyerImportListItem(FlyerImport flyerImport)
    {
        return new AdminFlyerImportListItem(
            flyerImport.Id,
            flyerImport.Status,
            flyerImport.SourcePlatform,
            flyerImport.Title,
            flyerImport.TeamName ?? flyerImport.OrganizationName,
            flyerImport.Sport?.Name ?? flyerImport.SportName,
            flyerImport.EventDate,
            flyerImport.City,
            flyerImport.State,
            flyerImport.ZipCode,
            !string.IsNullOrWhiteSpace(flyerImport.StoredObjectKey),
            flyerImport.TeamId,
            flyerImport.OpportunityId,
            flyerImport.CreatedAt,
            flyerImport.UpdatedAt);
    }

    private static AdminFlyerImportDetailItem ToFlyerImportDetailItem(FlyerImport flyerImport)
    {
        return new AdminFlyerImportDetailItem(
            flyerImport.Id,
            flyerImport.Status,
            flyerImport.SourcePlatform,
            flyerImport.SourceUrl,
            flyerImport.OriginalExternalImageUrl,
            !string.IsNullOrWhiteSpace(flyerImport.StoredObjectKey),
            flyerImport.StoredFileName,
            flyerImport.StoredContentType,
            flyerImport.ContentHash,
            flyerImport.TeamId,
            flyerImport.OpportunityId,
            flyerImport.ReviewedByUser is null ? null : GetDisplayName(flyerImport.ReviewedByUser),
            flyerImport.ReviewedAt,
            flyerImport.CreatedAt,
            flyerImport.UpdatedAt);
    }

    private static AdminFlyerImportForm ToFlyerImportForm(FlyerImport flyerImport)
    {
        return new AdminFlyerImportForm
        {
            SourcePlatform = flyerImport.SourcePlatform,
            SourceUrl = flyerImport.SourceUrl,
            OriginalExternalImageUrl = flyerImport.OriginalExternalImageUrl,
            SportId = flyerImport.SportId,
            SportName = flyerImport.SportName,
            OpportunityType = flyerImport.OpportunityType,
            Title = flyerImport.Title,
            TeamName = flyerImport.TeamName,
            OrganizationName = flyerImport.OrganizationName,
            AgeGroup = flyerImport.AgeGroup,
            CompetitionLevel = flyerImport.CompetitionLevel,
            EventDate = flyerImport.EventDate,
            EventEndDate = flyerImport.EventEndDate,
            RegistrationDeadline = flyerImport.RegistrationDeadline,
            RegistrationFee = flyerImport.RegistrationFee,
            Location = flyerImport.Location,
            Address = flyerImport.Address,
            City = flyerImport.City,
            State = flyerImport.State,
            ZipCode = flyerImport.ZipCode,
            ContactEmail = flyerImport.ContactEmail,
            ContactPhone = flyerImport.ContactPhone,
            WebsiteUrl = flyerImport.WebsiteUrl,
            Description = flyerImport.Description,
            RequiredEquipment = flyerImport.RequiredEquipment,
            WhatToBring = flyerImport.WhatToBring,
            SpecialInstructions = flyerImport.SpecialInstructions,
            ExtractedJson = flyerImport.ExtractedJson,
            ConfidenceJson = flyerImport.ConfidenceJson,
            AdminNotes = flyerImport.AdminNotes
        };
    }

    private static FlyerImportCreateInput ToFlyerImportInput(AdminFlyerImportForm form)
    {
        return new FlyerImportCreateInput(
            form.SourcePlatform,
            form.SourceUrl,
            form.OriginalExternalImageUrl,
            form.SportId,
            form.SportName,
            form.OpportunityType,
            form.Title,
            form.TeamName,
            form.OrganizationName,
            form.AgeGroup,
            form.CompetitionLevel,
            form.EventDate,
            form.EventEndDate,
            form.RegistrationDeadline,
            form.RegistrationFee,
            form.Location,
            form.Address,
            form.City,
            form.State,
            form.ZipCode,
            form.ContactEmail,
            form.ContactPhone,
            form.WebsiteUrl,
            form.Description,
            form.RequiredEquipment,
            form.WhatToBring,
            form.SpecialInstructions,
            form.ExtractedJson,
            form.ConfidenceJson,
            form.AdminNotes);
    }

    private static FlyerImportCreateInput MergeFlyerExtractionInput(
        AdminFlyerImportForm form,
        FlyerImportCreateInput extraction,
        string? extractedJson,
        string? confidenceJson)
    {
        return new FlyerImportCreateInput(
            form.SourcePlatform,
            form.SourceUrl,
            form.OriginalExternalImageUrl,
            form.SportId ?? extraction.SportId,
            form.SportName ?? extraction.SportName,
            form.OpportunityType ?? extraction.OpportunityType,
            form.Title ?? extraction.Title,
            form.TeamName ?? extraction.TeamName,
            form.OrganizationName ?? extraction.OrganizationName,
            form.AgeGroup ?? extraction.AgeGroup,
            form.CompetitionLevel ?? extraction.CompetitionLevel,
            form.EventDate ?? extraction.EventDate,
            form.EventEndDate ?? extraction.EventEndDate,
            form.RegistrationDeadline ?? extraction.RegistrationDeadline,
            form.RegistrationFee ?? extraction.RegistrationFee,
            form.Location ?? extraction.Location,
            form.Address ?? extraction.Address,
            form.City ?? extraction.City,
            form.State ?? extraction.State,
            form.ZipCode ?? extraction.ZipCode,
            form.ContactEmail ?? extraction.ContactEmail,
            form.ContactPhone ?? extraction.ContactPhone,
            form.WebsiteUrl ?? extraction.WebsiteUrl,
            form.Description ?? extraction.Description,
            form.RequiredEquipment ?? extraction.RequiredEquipment,
            form.WhatToBring ?? extraction.WhatToBring,
            form.SpecialInstructions ?? extraction.SpecialInstructions,
            extractedJson ?? extraction.ExtractedJson,
            confidenceJson ?? extraction.ConfidenceJson,
            form.AdminNotes ?? extraction.AdminNotes);
    }

    private static AdminTeamEditPageModel BuildTeamEditPageModel(Team team, string? returnUrl)
    {
        return new AdminTeamEditPageModel
        {
            TeamId = team.Id,
            ReturnUrl = returnUrl,
            Form = new AdminTeamEditForm
            {
                Name = team.Name,
                TeamLevel = team.TeamLevel,
                GeographicScope = team.GeographicScope,
                Description = team.Description,
                WebsiteUrl = team.WebsiteUrl,
                Address = team.Address,
                City = team.City,
                State = team.State,
                ZipCode = team.ZipCode,
                PhoneNumber = team.PhoneNumber,
                Email = team.Email,
                IsSearchable = team.IsSearchable,
                IsContactInfoVisible = team.IsContactInfoVisible,
                IsActive = team.IsActive
            }
        };
    }

    private async Task<AdminTeamOpportunityEditPageModel> BuildTeamOpportunityEditPageModelAsync(
        Opportunity opportunity,
        AdminTeamOpportunityEditForm form,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        return new AdminTeamOpportunityEditPageModel
        {
            OpportunityId = opportunity.Id,
            TeamId = opportunity.TeamId,
            ReturnUrl = returnUrl,
            Form = form,
            Flyer = await BuildTeamOpportunityFlyerReferenceAsync(opportunity, cancellationToken),
            SportOptions = await GetSportOptionsAsync(cancellationToken)
        };
    }

    private static AdminTeamOpportunityEditForm BuildTeamOpportunityEditForm(Opportunity opportunity)
    {
        return new AdminTeamOpportunityEditForm
        {
            TeamName = opportunity.Team.Name,
            TeamLevel = opportunity.Team.TeamLevel,
            TeamAddress = opportunity.Team.Address,
            TeamCity = opportunity.Team.City,
            TeamState = opportunity.Team.State,
            TeamZipCode = opportunity.Team.ZipCode,
            Title = opportunity.Title,
            Type = opportunity.Type,
            SportId = opportunity.SportId,
            AgeGroup = opportunity.AgeGroup,
            CompetitionLevel = opportunity.CompetitionLevel,
            Description = opportunity.Description,
            EventDate = opportunity.EventDate,
            EventEndDate = opportunity.EventEndDate,
            ListingStartDate = opportunity.ListingStartDate,
            ListingEndDate = opportunity.ListingEndDate ?? opportunity.ExpiresAt,
            Location = opportunity.Location,
            Address = opportunity.Address,
            City = opportunity.City,
            State = opportunity.State,
            ZipCode = opportunity.ZipCode,
            ContactEmail = opportunity.ContactEmail,
            ContactPhone = opportunity.ContactPhone,
            WebsiteUrl = opportunity.WebsiteUrl,
            RequiredEquipment = opportunity.RequiredEquipment,
            WhatToBring = opportunity.WhatToBring,
            SpecialInstructions = opportunity.SpecialInstructions,
            IsPublished = opportunity.IsPublished,
            IsActive = opportunity.IsActive
        };
    }

    private async Task ValidateTeamOpportunityEditFormAsync(
        Opportunity opportunity,
        AdminTeamOpportunityEditForm form,
        CancellationToken cancellationToken)
    {
        var normalizedType = NormalizeOpportunityType(form.Type);
        if (!OpportunityTypeOptions.Contains(normalizedType, StringComparer.Ordinal))
        {
            ModelState.AddModelError("Form.Type", "Choose a supported opportunity type.");
        }

        var sportExists = await dbContext.Sports
            .AsNoTracking()
            .AnyAsync(sport => sport.Id == form.SportId && sport.IsActive, cancellationToken);
        if (!sportExists)
        {
            ModelState.AddModelError("Form.SportId", "Selected sport is not available.");
        }

        var normalizedEventDate = NormalizeUtc(form.EventDate);
        var normalizedEventEndDate = NormalizeUtc(form.EventEndDate);
        var normalizedListingStartDate = NormalizeUtc(form.ListingStartDate);
        var normalizedListingEndDate = NormalizeUtc(form.ListingEndDate);

        if (normalizedEventDate.HasValue
            && normalizedEventEndDate.HasValue
            && normalizedEventEndDate.Value < normalizedEventDate.Value)
        {
            ModelState.AddModelError("Form.EventEndDate", "Event end date cannot be earlier than event start date.");
        }

        if (normalizedListingStartDate.HasValue
            && normalizedListingEndDate.HasValue
            && normalizedListingEndDate.Value < normalizedListingStartDate.Value)
        {
            ModelState.AddModelError("Form.ListingEndDate", "Listing end date cannot be earlier than listing start date.");
        }

        if (form.IsPublished && form.IsActive)
        {
            if (!opportunity.IsPublished
                && !await IsFlyerCreatedOpportunityAsync(opportunity.Id, cancellationToken))
            {
                ModelState.AddModelError(
                    "Form.IsPublished",
                    "Team-created opportunities must be published from the team account so posting rules are applied. Admin publishing is only for flyer-created opportunities.");
            }

            if (normalizedEventDate is null)
            {
                ModelState.AddModelError("Form.EventDate", "Event date is required before publishing.");
            }

            if (string.IsNullOrWhiteSpace(form.ZipCode))
            {
                ModelState.AddModelError("Form.ZipCode", "ZIP code is required before publishing.");
            }

            var effectiveEndDate = normalizedListingEndDate
                ?? ResolveOpportunityExpiration(normalizedEventDate, normalizedEventEndDate);
            if (effectiveEndDate.HasValue && effectiveEndDate.Value <= DateTime.UtcNow)
            {
                ModelState.AddModelError("Form.ListingEndDate", "Listing end date must be in the future before publishing.");
            }
        }
    }

    private Task<bool> IsFlyerCreatedOpportunityAsync(Guid opportunityId, CancellationToken cancellationToken)
    {
        return dbContext.FlyerImports
            .AsNoTracking()
            .AnyAsync(flyerImport => flyerImport.OpportunityId == opportunityId, cancellationToken);
    }

    private async Task SyncFlyerImportsForOpportunityAsync(
        Opportunity opportunity,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var flyerImports = await dbContext.FlyerImports
            .Where(flyerImport => flyerImport.OpportunityId == opportunity.Id)
            .ToArrayAsync(cancellationToken);
        foreach (var flyerImport in flyerImports)
        {
            flyerImport.TeamId = opportunity.TeamId;
            flyerImport.SportId = opportunity.SportId;
            flyerImport.OpportunityType = opportunity.Type;
            flyerImport.Title = opportunity.Title;
            flyerImport.TeamName = opportunity.Team.Name;
            flyerImport.AgeGroup = opportunity.AgeGroup;
            flyerImport.CompetitionLevel = opportunity.CompetitionLevel;
            flyerImport.EventDate = opportunity.EventDate;
            flyerImport.EventEndDate = opportunity.EventEndDate;
            flyerImport.Location = opportunity.Location;
            flyerImport.Address = opportunity.Address;
            flyerImport.City = opportunity.City;
            flyerImport.State = opportunity.State;
            flyerImport.ZipCode = opportunity.ZipCode;
            flyerImport.ContactEmail = opportunity.ContactEmail;
            flyerImport.ContactPhone = opportunity.ContactPhone;
            flyerImport.WebsiteUrl = opportunity.WebsiteUrl;
            flyerImport.Description = opportunity.Description;
            flyerImport.RequiredEquipment = opportunity.RequiredEquipment;
            flyerImport.WhatToBring = opportunity.WhatToBring;
            flyerImport.SpecialInstructions = opportunity.SpecialInstructions;
            flyerImport.Status = opportunity.IsPublished
                ? TryOutSpotFlyerImportStatuses.Published
                : TryOutSpotFlyerImportStatuses.DraftCreated;
            flyerImport.UpdatedAt = now;
        }
    }

    private async Task<AdminTeamOpportunityFlyerReference?> BuildTeamOpportunityFlyerReferenceAsync(
        Opportunity opportunity,
        CancellationToken cancellationToken)
    {
        var flyerImport = await dbContext.FlyerImports
            .AsNoTracking()
            .Where(currentImport => currentImport.OpportunityId == opportunity.Id)
            .OrderByDescending(currentImport => currentImport.UpdatedAt)
            .Select(currentImport => new
            {
                currentImport.StoredObjectKey,
                currentImport.StoredFileName,
                currentImport.StoredContentType,
                currentImport.SourceUrl,
                currentImport.OriginalExternalImageUrl
            })
            .FirstOrDefaultAsync(cancellationToken);

        var hasOpportunityFlyer = !string.IsNullOrWhiteSpace(opportunity.UploadedPdfObjectKey);
        var hasImportFlyer = !string.IsNullOrWhiteSpace(flyerImport?.StoredObjectKey);
        var storedFlyerUrl = hasOpportunityFlyer || hasImportFlyer
            ? $"/admin/team-opportunities/{opportunity.Id}/flyer"
            : null;
        var fileName = hasOpportunityFlyer
            ? opportunity.UploadedPdfFileName
            : flyerImport?.StoredFileName;
        var contentType = hasOpportunityFlyer
            ? ResolveFlyerImportContentType(null, null, opportunity.UploadedPdfFileName ?? opportunity.UploadedPdfObjectKey)
            : ResolveFlyerImportContentType(null, flyerImport?.StoredContentType, flyerImport?.StoredFileName);

        if (storedFlyerUrl is null
            && string.IsNullOrWhiteSpace(flyerImport?.SourceUrl)
            && string.IsNullOrWhiteSpace(flyerImport?.OriginalExternalImageUrl))
        {
            return null;
        }

        return new AdminTeamOpportunityFlyerReference(
            fileName,
            contentType,
            storedFlyerUrl,
            flyerImport?.SourceUrl,
            flyerImport?.OriginalExternalImageUrl);
    }

    private async Task<TeamOpportunityFlyerDownload?> ResolveTeamOpportunityFlyerDownloadAsync(
        Guid opportunityId,
        CancellationToken cancellationToken)
    {
        var opportunity = await dbContext.Opportunities
            .AsNoTracking()
            .Where(currentOpportunity => currentOpportunity.Id == opportunityId)
            .Select(currentOpportunity => new
            {
                currentOpportunity.UploadedPdfObjectKey,
                currentOpportunity.UploadedPdfFileName
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (opportunity is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(opportunity.UploadedPdfObjectKey))
        {
            return new TeamOpportunityFlyerDownload(
                opportunity.UploadedPdfObjectKey,
                opportunity.UploadedPdfFileName,
                null);
        }

        var flyerImport = await dbContext.FlyerImports
            .AsNoTracking()
            .Where(currentImport => currentImport.OpportunityId == opportunityId)
            .OrderByDescending(currentImport => currentImport.UpdatedAt)
            .Select(currentImport => new
            {
                currentImport.StoredObjectKey,
                currentImport.StoredFileName,
                currentImport.StoredContentType
            })
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(flyerImport?.StoredObjectKey)
            ? null
            : new TeamOpportunityFlyerDownload(
                flyerImport.StoredObjectKey,
                flyerImport.StoredFileName,
                flyerImport.StoredContentType);
    }

    private static AdminFlyerImportDetailItem BuildNewFlyerImportDetailItem()
    {
        var now = DateTime.UtcNow;
        return new AdminFlyerImportDetailItem(
            Guid.Empty,
            TryOutSpotFlyerImportStatuses.PendingReview,
            "facebook",
            null,
            null,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            now,
            now);
    }

    private void AddModelErrors(IReadOnlyCollection<string> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }
    }

    private static string ResolveFlyerImportContentType(
        string? downloadedContentType,
        string? storedContentType,
        string? fileName)
    {
        return NormalizeContentType(downloadedContentType)
            ?? NormalizeContentType(storedContentType)
            ?? ResolveContentTypeFromExtension(fileName)
            ?? "application/octet-stream";
    }

    private static string? NormalizeContentType(string? contentType)
    {
        var normalizedContentType = NormalizeOptional(contentType)?.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (string.Equals(normalizedContentType, "image/jpg", StringComparison.Ordinal))
        {
            normalizedContentType = "image/jpeg";
        }

        return normalizedContentType is "application/pdf" or "image/jpeg" or "image/png" or "image/webp"
            ? normalizedContentType
            : null;
    }

    private static string? ResolveContentTypeFromExtension(string? fileName)
    {
        var normalizedFileName = NormalizeOptional(fileName);
        return normalizedFileName is null
            ? null
            : Path.GetExtension(normalizedFileName).ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                _ => null
            };
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

    private static AdminTeamOpportunityListItem ToTeamOpportunityItem(
        Opportunity opportunity,
        bool isFlyerCreated = false)
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
            opportunity.Team.IsActive,
            opportunity.IsActive,
            isFlyerCreated,
            opportunity.EventDate,
            opportunity.ListingStartDate,
            opportunity.ListingEndDate,
            opportunity.ExpiresAt,
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

    private bool ValidatePromotionSettingsForm(AdminPromotionSettingsForm form)
    {
        if (form.MaxRedemptions is < LaunchPromotionStatusService.MinMaxRedemptions
            or > LaunchPromotionStatusService.MaxMaxRedemptions)
        {
            TempData["StatusMessage"] = "Promotion claim limit must be between 1 and 100000.";
            return false;
        }

        if (form.GrantMonths is < LaunchPromotionStatusService.MinGrantMonths
            or > LaunchPromotionStatusService.MaxGrantMonths)
        {
            TempData["StatusMessage"] = "Promotion duration must be between 1 and 120 months.";
            return false;
        }

        return true;
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

    private static string? NormalizeLength(string? value, int maxLength)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return null;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private static string? NormalizeState(string? state)
    {
        var normalized = NormalizeLength(state, 2);
        return normalized?.ToUpperInvariant();
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

    private static string NormalizeOpportunityType(string? opportunityType)
    {
        var normalized = NormalizeOptional(opportunityType)?
            .ToLowerInvariant()
            .Replace('-', '_')
            .Replace(' ', '_');
        if (normalized is not null && OpportunityTypeOptions.Contains(normalized, StringComparer.Ordinal))
        {
            return normalized;
        }

        var text = normalized ?? string.Empty;
        if (text.Contains("add", StringComparison.Ordinal)
            || text.Contains("adding", StringComparison.Ordinal)
            || text.Contains("player_needed", StringComparison.Ordinal)
            || text.Contains("need_player", StringComparison.Ordinal)
            || text.Contains("need_players", StringComparison.Ordinal)
            || text.Contains("roster", StringComparison.Ordinal))
        {
            return "roster_opening";
        }

        if (text.Contains("guest", StringComparison.Ordinal)
            || text.Contains("sub", StringComparison.Ordinal)
            || text.Contains("fill_in", StringComparison.Ordinal)
            || text.Contains("pickup", StringComparison.Ordinal))
        {
            return "pickup_player";
        }

        if (text.Contains("camp", StringComparison.Ordinal))
        {
            return "camp";
        }

        if (text.Contains("clinic", StringComparison.Ordinal))
        {
            return "clinic";
        }

        if (text.Contains("tournament", StringComparison.Ordinal))
        {
            return "tournament";
        }

        if (text.Contains("private", StringComparison.Ordinal))
        {
            return "private_workout";
        }

        if (text.Contains("other", StringComparison.Ordinal))
        {
            return "other";
        }

        return "tryout";
    }

    private static DateTime? ResolveOpportunityExpiration(DateTime? eventDate, DateTime? eventEndDate)
    {
        if (eventEndDate.HasValue)
        {
            return eventEndDate.Value.AddDays(1);
        }

        return eventDate?.AddDays(1);
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

    private sealed record TeamOpportunityFlyerDownload(
        string ObjectKey,
        string? FileName,
        string? ContentType);
}
