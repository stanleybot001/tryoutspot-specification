using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Listings;
using TryOutSpot.Web.Models.Listings;
using TryOutSpot.Web.Models.TeamListings;
using TryOutSpot.Web.Security;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Controllers;

/// <summary>
/// Team and organization listing endpoints for managed opportunity posting.
/// </summary>
[ApiController]
[Tags("Team Listings")]
[Produces("application/json")]
[Route("api/team-listings")]
[Authorize(Policy = TryOutSpotAuthorizationPolicies.ActiveUser)]
public sealed class TeamListingsApiController(
    AppDbContext dbContext,
    IEntitlementService entitlementService,
    IZipRadiusSearchService zipRadiusSearchService) : ControllerBase
{
    private const int FreeCoachPublishingLimit = 1;
    private const int FreeCoachPublishingWindowMonths = 6;
    private const int BasicTeamPublishingLimit = 9;
    private const int BasicTeamPublishingWindowMonths = 12;
    private const int ProfessionalTeamPublishingLimit = 24;
    private const int ProfessionalTeamPublishingWindowMonths = 12;
    private const int EnterpriseTeamPublishingLimit = 50;
    private const int EnterpriseTeamPublishingWindowMonths = 12;
    private const string OpportunityWaiverDocumentType = "opportunity-waivers";

    /// <summary>
    /// Lists all active teams the signed-in account can manage.
    /// </summary>
    [HttpGet("mine")]
    [ProducesResponseType<ManagedTeamListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ManagedTeamListResponse>> Mine(CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var teamRoles = await dbContext.UserTeamRoles
            .AsNoTracking()
            .Where(teamRole => teamRole.UserId == userId)
            .Where(teamRole => teamRole.IsActive)
            .Where(teamRole => teamRole.Team.IsActive)
            .Include(teamRole => teamRole.Team)
                .ThenInclude(team => team.Organization)
            .Include(teamRole => teamRole.Team)
                .ThenInclude(team => team.TeamSports)
                    .ThenInclude(teamSport => teamSport.Sport)
            .Include(teamRole => teamRole.Team)
                .ThenInclude(team => team.Opportunities)
            .OrderBy(teamRole => teamRole.Team.Name)
            .ToArrayAsync(cancellationToken);

        var teams = teamRoles
            .Select(teamRole => ToManagedTeamSummary(teamRole.Team, teamRole.Role))
            .ToArray();

        return Ok(new ManagedTeamListResponse(teams));
    }

    /// <summary>
    /// Lists all active opportunities for one managed team.
    /// </summary>
    [HttpGet("mine/{teamId:guid}/opportunities")]
    [ProducesResponseType<TeamOpportunityListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TeamOpportunityListResponse>> MineOpportunities(
        Guid teamId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var managedTeam = await GetManagedTeamAsync(userId, teamId, cancellationToken);
        if (managedTeam is null)
        {
            return NotFound();
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.Opportunities
            .AsNoTracking()
            .Where(opportunity => opportunity.TeamId == teamId)
            .Where(opportunity => opportunity.IsActive);

        var totalCount = await query.CountAsync(cancellationToken);
        var opportunities = await query
            .Include(opportunity => opportunity.Sport)
            .OrderByDescending(opportunity => opportunity.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        var favoriteCountsByOpportunityId = await GetFavoriteCountsByOpportunityIdAsync(
            opportunities.Select(opportunity => opportunity.Id),
            cancellationToken);

        return Ok(new TeamOpportunityListResponse(
            teamId,
            opportunities
                .Select(opportunity => ToSummaryResponse(
                    opportunity,
                    favoriteCountsByOpportunityId.GetValueOrDefault(opportunity.Id)))
                .ToArray(),
            page,
            pageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    /// <summary>
    /// Returns one active managed team opportunity.
    /// </summary>
    [HttpGet("mine/{teamId:guid}/opportunities/{opportunityId:guid}")]
    [ProducesResponseType<TeamOpportunityDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TeamOpportunityDetailResponse>> MineOpportunityById(
        Guid teamId,
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var managedTeam = await GetManagedTeamAsync(userId, teamId, cancellationToken);
        if (managedTeam is null)
        {
            return NotFound();
        }

        var opportunity = await dbContext.Opportunities
            .AsNoTracking()
            .Where(currentOpportunity => currentOpportunity.Id == opportunityId)
            .Where(currentOpportunity => currentOpportunity.TeamId == teamId)
            .Where(currentOpportunity => currentOpportunity.IsActive)
            .Include(currentOpportunity => currentOpportunity.Sport)
            .SingleOrDefaultAsync(cancellationToken);

        if (opportunity is null)
        {
            return NotFound();
        }

        var favoriteCount = await GetOpportunityFavoriteCountAsync(opportunityId, cancellationToken);

        return Ok(ToDetailResponse(opportunity, favoriteCount));
    }

    /// <summary>
    /// Creates a team opportunity listing.
    /// </summary>
    [HttpPost("mine/{teamId:guid}/opportunities")]
    [ProducesResponseType<TeamOpportunityActionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TeamOpportunityActionResponse>> CreateOpportunity(
        Guid teamId,
        CreateTeamOpportunityRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var managedTeam = await GetManagedTeamAsync(userId, teamId, cancellationToken);
        if (managedTeam is null)
        {
            return NotFound();
        }

        var postingAccess = await ResolvePostingAccessAsync(userId, cancellationToken);
        if (!postingAccess.CanPostOpportunities)
        {
            return Forbid();
        }

        var canConfigureRegistration = await CanConfigureTryoutRegistrationAsync(userId, cancellationToken);
        var normalizedType = NormalizeOptional(request.Type);
        var isTryoutType = string.Equals(normalizedType, "tryout", StringComparison.OrdinalIgnoreCase);
        var normalizedRequestRegistrationRequired = canConfigureRegistration
            && isTryoutType
            && request.RegistrationRequired;
        var normalizedRequestRequiredFieldCodes = normalizedRequestRegistrationRequired
            ? NormalizeRegistrationFieldCodes(request.RequiredRegistrationFieldCodes)
            : [];
        var normalizedRequestWaiverRequired = normalizedRequestRegistrationRequired
            && (request.WaiverRequired
                || normalizedRequestRequiredFieldCodes.Contains(TryOutSpotOpportunityRegistrationFields.WaiverSignature, StringComparer.OrdinalIgnoreCase));
        var normalizedRequestWaiverMethod = normalizedRequestWaiverRequired
            ? TryOutSpotOpportunityWaiverMethods.Normalize(request.WaiverMethod) ?? TryOutSpotOpportunityWaiverMethods.AtEvent
            : null;
        var isDownloadableRequestWaiverMethod = normalizedRequestWaiverRequired
            && string.Equals(
                normalizedRequestWaiverMethod,
                TryOutSpotOpportunityWaiverMethods.Downloadable,
                StringComparison.Ordinal);
        var normalizedRequestWaiverReturnByEmail = isDownloadableRequestWaiverMethod && request.WaiverReturnByEmail;
        var normalizedRequestWaiverReturnInPerson = isDownloadableRequestWaiverMethod && request.WaiverReturnInPerson;
        var normalizedRequestMaxParticipants = normalizedRequestRegistrationRequired && request.MaxParticipants is > 0
            ? request.MaxParticipants
            : null;
        var normalizedContactEmail = NormalizeOptional(request.ContactEmail) ?? managedTeam.Team.Email;
        var normalizedRequiredRegistrationFieldCodes = NormalizeRequiredRegistrationFieldCodesForWaiver(
            normalizedRequestRequiredFieldCodes,
            normalizedRequestWaiverRequired);

        await ValidateOpportunityWriteRequestAsync(
            request.Type,
            request.SportId,
            canConfigureRegistration,
            normalizedRequestRegistrationRequired,
            normalizedRequiredRegistrationFieldCodes,
            normalizedRequestWaiverRequired,
            normalizedRequestWaiverMethod,
            normalizedRequestWaiverReturnByEmail,
            normalizedRequestWaiverReturnInPerson,
            normalizedContactEmail,
            normalizedRequestMaxParticipants,
            request.RegistrationDeadline,
            request.EventDate,
            request.EventEndDate,
            request.ListingStartDate,
            request.ListingEndDate,
            request.ExpiresAt,
            cancellationToken);

        var normalizedZipCode = ResolveZipCodeOrAddModelError(request.ZipCode, managedTeam.Team.ZipCode, nameof(request.ZipCode));
        if (request.IsPublished)
        {
            await EnsureCanPublishAsync(teamId, postingAccess, currentlyPublished: false, cancellationToken);
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var now = DateTime.UtcNow;
        var normalizedMaxParticipants = normalizedRequestMaxParticipants;
        var opportunity = new Opportunity
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            SportId = request.SportId,
            Type = request.Type.Trim(),
            Title = request.Title.Trim(),
            Description = NormalizeOptional(request.Description),
            CompetitionLevel = NormalizeOptional(request.CompetitionLevel),
            AgeGroup = NormalizeOptional(request.AgeGroup),
            RegistrationRequired = normalizedRequestRegistrationRequired,
            MaxParticipants = normalizedMaxParticipants,
            RegistrationRequiredFieldCodes = SerializeRegistrationFieldCodes(normalizedRequiredRegistrationFieldCodes),
            WaiverRequired = normalizedRequestWaiverRequired,
            WaiverMethod = normalizedRequestWaiverMethod,
            WaiverReturnByEmail = normalizedRequestWaiverReturnByEmail,
            WaiverReturnInPerson = normalizedRequestWaiverReturnInPerson,
            RegistrationDeadline = NormalizeUtc(request.RegistrationDeadline),
            RegistrationFee = request.RegistrationFee,
            EventDate = NormalizeUtc(request.EventDate),
            EventEndDate = NormalizeUtc(request.EventEndDate),
            ListingStartDate = NormalizeUtc(request.ListingStartDate),
            ListingEndDate = NormalizeUtc(request.ListingEndDate),
            Location = NormalizeOptional(request.Location),
            Address = NormalizeOptional(request.Address),
            City = NormalizeOptional(request.City) ?? managedTeam.Team.City,
            State = NormalizeState(request.State) ?? managedTeam.Team.State,
            ZipCode = normalizedZipCode,
            ContactEmail = normalizedContactEmail,
            ContactPhone = NormalizeOptional(request.ContactPhone) ?? managedTeam.Team.PhoneNumber,
            WebsiteUrl = NormalizeOptional(request.WebsiteUrl) ?? managedTeam.Team.WebsiteUrl,
            PdfUrl = NormalizeOptional(request.PdfUrl),
            RequiredEquipment = NormalizeOptional(request.RequiredEquipment),
            WhatToBring = NormalizeOptional(request.WhatToBring),
            SpecialInstructions = NormalizeOptional(request.SpecialInstructions),
            IsPublished = request.IsPublished,
            PublishedAt = request.IsPublished ? now : null,
            ExpiresAt = NormalizeUtc(request.ListingEndDate) ?? NormalizeUtc(request.ExpiresAt),
            ViewCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };

        dbContext.Opportunities.Add(opportunity);
        await dbContext.SaveChangesAsync(cancellationToken);

        var created = await dbContext.Opportunities
            .AsNoTracking()
            .Where(currentOpportunity => currentOpportunity.Id == opportunity.Id)
            .Include(currentOpportunity => currentOpportunity.Sport)
            .SingleAsync(cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            new TeamOpportunityActionResponse("Opportunity created.", ToDetailResponse(created, favoriteCount: 0)));
    }

    /// <summary>
    /// Updates an existing team opportunity listing.
    /// </summary>
    [HttpPost("mine/{teamId:guid}/opportunities/{opportunityId:guid}/update")]
    [ProducesResponseType<TeamOpportunityActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TeamOpportunityActionResponse>> UpdateOpportunity(
        Guid teamId,
        Guid opportunityId,
        UpdateTeamOpportunityRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var managedTeam = await GetManagedTeamAsync(userId, teamId, cancellationToken);
        if (managedTeam is null)
        {
            return NotFound();
        }

        var postingAccess = await ResolvePostingAccessAsync(userId, cancellationToken);
        if (!postingAccess.CanPostOpportunities)
        {
            return Forbid();
        }

        var canConfigureRegistration = await CanConfigureTryoutRegistrationAsync(userId, cancellationToken);
        var normalizedType = NormalizeOptional(request.Type);
        var isTryoutType = string.Equals(normalizedType, "tryout", StringComparison.OrdinalIgnoreCase);
        var normalizedRequestRegistrationRequired = canConfigureRegistration
            && isTryoutType
            && request.RegistrationRequired;
        var normalizedRequestRequiredFieldCodes = normalizedRequestRegistrationRequired
            ? NormalizeRegistrationFieldCodes(request.RequiredRegistrationFieldCodes)
            : [];
        var normalizedRequestWaiverRequired = normalizedRequestRegistrationRequired
            && (request.WaiverRequired
                || normalizedRequestRequiredFieldCodes.Contains(TryOutSpotOpportunityRegistrationFields.WaiverSignature, StringComparer.OrdinalIgnoreCase));
        var normalizedRequestWaiverMethod = normalizedRequestWaiverRequired
            ? TryOutSpotOpportunityWaiverMethods.Normalize(request.WaiverMethod) ?? TryOutSpotOpportunityWaiverMethods.AtEvent
            : null;
        var isDownloadableRequestWaiverMethod = normalizedRequestWaiverRequired
            && string.Equals(
                normalizedRequestWaiverMethod,
                TryOutSpotOpportunityWaiverMethods.Downloadable,
                StringComparison.Ordinal);
        var normalizedRequestWaiverReturnByEmail = isDownloadableRequestWaiverMethod && request.WaiverReturnByEmail;
        var normalizedRequestWaiverReturnInPerson = isDownloadableRequestWaiverMethod && request.WaiverReturnInPerson;
        var normalizedRequestMaxParticipants = normalizedRequestRegistrationRequired && request.MaxParticipants is > 0
            ? request.MaxParticipants
            : null;
        var normalizedContactEmail = NormalizeOptional(request.ContactEmail) ?? managedTeam.Team.Email;
        var normalizedRequiredRegistrationFieldCodes = NormalizeRequiredRegistrationFieldCodesForWaiver(
            normalizedRequestRequiredFieldCodes,
            normalizedRequestWaiverRequired);

        var opportunity = await dbContext.Opportunities
            .SingleOrDefaultAsync(currentOpportunity =>
                currentOpportunity.Id == opportunityId
                && currentOpportunity.TeamId == teamId
                && currentOpportunity.IsActive,
                cancellationToken);
        if (opportunity is null)
        {
            return NotFound();
        }

        await ValidateOpportunityWriteRequestAsync(
            request.Type,
            request.SportId,
            canConfigureRegistration,
            normalizedRequestRegistrationRequired,
            normalizedRequiredRegistrationFieldCodes,
            normalizedRequestWaiverRequired,
            normalizedRequestWaiverMethod,
            normalizedRequestWaiverReturnByEmail,
            normalizedRequestWaiverReturnInPerson,
            normalizedContactEmail,
            normalizedRequestMaxParticipants,
            request.RegistrationDeadline,
            request.EventDate,
            request.EventEndDate,
            request.ListingStartDate,
            request.ListingEndDate,
            request.ExpiresAt,
            cancellationToken);

        var normalizedZipCode = ResolveZipCodeOrAddModelError(request.ZipCode, managedTeam.Team.ZipCode, nameof(request.ZipCode));
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var normalizedMaxParticipants = normalizedRequestMaxParticipants;

        opportunity.SportId = request.SportId;
        opportunity.Type = request.Type.Trim();
        opportunity.Title = request.Title.Trim();
        opportunity.Description = NormalizeOptional(request.Description);
        opportunity.CompetitionLevel = NormalizeOptional(request.CompetitionLevel);
        opportunity.AgeGroup = NormalizeOptional(request.AgeGroup);
        opportunity.RegistrationRequired = normalizedRequestRegistrationRequired;
        opportunity.MaxParticipants = normalizedMaxParticipants;
        opportunity.RegistrationRequiredFieldCodes = SerializeRegistrationFieldCodes(normalizedRequiredRegistrationFieldCodes);
        opportunity.WaiverRequired = normalizedRequestWaiverRequired;
        opportunity.WaiverMethod = normalizedRequestWaiverMethod;
        opportunity.WaiverReturnByEmail = normalizedRequestWaiverReturnByEmail;
        opportunity.WaiverReturnInPerson = normalizedRequestWaiverReturnInPerson;
        opportunity.RegistrationDeadline = NormalizeUtc(request.RegistrationDeadline);
        opportunity.RegistrationFee = request.RegistrationFee;
        opportunity.EventDate = NormalizeUtc(request.EventDate);
        opportunity.EventEndDate = NormalizeUtc(request.EventEndDate);
        opportunity.ListingStartDate = NormalizeUtc(request.ListingStartDate);
        opportunity.ListingEndDate = NormalizeUtc(request.ListingEndDate);
        opportunity.Location = NormalizeOptional(request.Location);
        opportunity.Address = NormalizeOptional(request.Address);
        opportunity.City = NormalizeOptional(request.City) ?? managedTeam.Team.City;
        opportunity.State = NormalizeState(request.State) ?? managedTeam.Team.State;
        opportunity.ZipCode = normalizedZipCode;
        opportunity.ContactEmail = normalizedContactEmail;
        opportunity.ContactPhone = NormalizeOptional(request.ContactPhone) ?? managedTeam.Team.PhoneNumber;
        opportunity.WebsiteUrl = NormalizeOptional(request.WebsiteUrl) ?? managedTeam.Team.WebsiteUrl;
        opportunity.PdfUrl = NormalizeOptional(request.PdfUrl);
        opportunity.RequiredEquipment = NormalizeOptional(request.RequiredEquipment);
        opportunity.WhatToBring = NormalizeOptional(request.WhatToBring);
        opportunity.SpecialInstructions = NormalizeOptional(request.SpecialInstructions);
        opportunity.ExpiresAt = NormalizeUtc(request.ListingEndDate) ?? NormalizeUtc(request.ExpiresAt);
        opportunity.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        var updated = await dbContext.Opportunities
            .AsNoTracking()
            .Where(currentOpportunity => currentOpportunity.Id == opportunityId)
            .Include(currentOpportunity => currentOpportunity.Sport)
            .SingleAsync(cancellationToken);
        var favoriteCount = await GetOpportunityFavoriteCountAsync(opportunityId, cancellationToken);

        return Ok(new TeamOpportunityActionResponse("Opportunity updated.", ToDetailResponse(updated, favoriteCount)));
    }

    /// <summary>
    /// Publishes or unpublishes a managed team opportunity listing.
    /// </summary>
    [HttpPost("mine/{teamId:guid}/opportunities/{opportunityId:guid}/publication")]
    [ProducesResponseType<TeamOpportunityActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TeamOpportunityActionResponse>> SetPublication(
        Guid teamId,
        Guid opportunityId,
        SetTeamOpportunityPublicationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var managedTeam = await GetManagedTeamAsync(userId, teamId, cancellationToken);
        if (managedTeam is null)
        {
            return NotFound();
        }

        var opportunity = await dbContext.Opportunities
            .SingleOrDefaultAsync(currentOpportunity =>
                currentOpportunity.Id == opportunityId
                && currentOpportunity.TeamId == teamId
                && currentOpportunity.IsActive,
                cancellationToken);
        if (opportunity is null)
        {
            return NotFound();
        }

        if (request.IsPublished)
        {
            var postingAccess = await ResolvePostingAccessAsync(userId, cancellationToken);
            if (!postingAccess.CanPostOpportunities)
            {
                return Forbid();
            }

            await EnsureCanPublishAsync(teamId, postingAccess, opportunity.IsPublished, cancellationToken);
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }
        }

        var now = DateTime.UtcNow;
        opportunity.IsPublished = request.IsPublished;
        opportunity.PublishedAt = request.IsPublished
            ? opportunity.PublishedAt ?? now
            : opportunity.PublishedAt;
        opportunity.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        var updated = await dbContext.Opportunities
            .AsNoTracking()
            .Where(currentOpportunity => currentOpportunity.Id == opportunityId)
            .Include(currentOpportunity => currentOpportunity.Sport)
            .SingleAsync(cancellationToken);
        var favoriteCount = await GetOpportunityFavoriteCountAsync(opportunityId, cancellationToken);

        return Ok(new TeamOpportunityActionResponse(
            request.IsPublished ? "Opportunity published." : "Opportunity unpublished.",
            ToDetailResponse(updated, favoriteCount)));
    }

    /// <summary>
    /// Deactivates a managed team opportunity listing.
    /// </summary>
    [HttpPost("mine/{teamId:guid}/opportunities/{opportunityId:guid}/deactivate")]
    [ProducesResponseType<TeamOpportunityActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<TeamOpportunityActionResponse>> Deactivate(
        Guid teamId,
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var managedTeam = await GetManagedTeamAsync(userId, teamId, cancellationToken);
        if (managedTeam is null)
        {
            return NotFound();
        }

        var opportunity = await dbContext.Opportunities
            .SingleOrDefaultAsync(currentOpportunity =>
                currentOpportunity.Id == opportunityId
                && currentOpportunity.TeamId == teamId
                && currentOpportunity.IsActive,
                cancellationToken);
        if (opportunity is null)
        {
            return NotFound();
        }

        opportunity.IsActive = false;
        opportunity.IsPublished = false;
        opportunity.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var updated = await dbContext.Opportunities
            .AsNoTracking()
            .Where(currentOpportunity => currentOpportunity.Id == opportunityId)
            .Include(currentOpportunity => currentOpportunity.Sport)
            .SingleAsync(cancellationToken);
        var favoriteCount = await GetOpportunityFavoriteCountAsync(opportunityId, cancellationToken);

        return Ok(new TeamOpportunityActionResponse("Opportunity deactivated.", ToDetailResponse(updated, favoriteCount)));
    }

    /// <summary>
    /// Reports a published team opportunity for platform administrator review.
    /// </summary>
    [HttpPost("opportunities/{opportunityId:guid}/report")]
    [ProducesResponseType<ListingReportActionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ListingReportActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ListingReportActionResponse>> ReportOpportunity(
        Guid opportunityId,
        ReportListingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var reason = NormalizeOptional(request.Reason);
        if (reason is null)
        {
            ModelState.AddModelError(nameof(request.Reason), "A report reason is required.");
            return ValidationProblem(ModelState);
        }

        var opportunity = await dbContext.Opportunities
            .AsNoTracking()
            .Where(currentOpportunity => currentOpportunity.Id == opportunityId)
            .Where(currentOpportunity => currentOpportunity.IsActive)
            .Where(currentOpportunity => currentOpportunity.IsPublished)
            .Select(currentOpportunity => new
            {
                currentOpportunity.Id,
                currentOpportunity.TeamId
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (opportunity is null)
        {
            return NotFound();
        }

        var userManagesTeam = await dbContext.UserTeamRoles
            .AsNoTracking()
            .AnyAsync(
                teamRole => teamRole.UserId == userId
                    && teamRole.TeamId == opportunity.TeamId
                    && teamRole.IsActive,
                cancellationToken);
        if (userManagesTeam)
        {
            ModelState.AddModelError(nameof(opportunityId), "You cannot report an opportunity for a team you manage.");
            return ValidationProblem(ModelState);
        }

        var existingReport = await dbContext.ListingReports
            .AsNoTracking()
            .Where(report => report.ReporterUserId == userId)
            .Where(report => report.OpportunityId == opportunityId)
            .Where(report => report.Status == TryOutSpotListingReportStatuses.Pending
                || report.Status == TryOutSpotListingReportStatuses.InReview)
            .OrderByDescending(report => report.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingReport is not null)
        {
            return Ok(new ListingReportActionResponse(
                "This listing is already in review from your report.",
                ToReportResponse(existingReport, TryOutSpotListingReportTargetTypes.TeamOpportunity, opportunityId)));
        }

        var now = DateTime.UtcNow;
        var report = new ListingReport
        {
            Id = Guid.NewGuid(),
            ReporterUserId = userId,
            OpportunityId = opportunityId,
            Reason = reason,
            Details = NormalizeOptional(request.Details),
            Status = TryOutSpotListingReportStatuses.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.ListingReports.Add(report);
        await dbContext.SaveChangesAsync(cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            new ListingReportActionResponse(
                "Thanks. The listing has been sent to platform review.",
                ToReportResponse(report, TryOutSpotListingReportTargetTypes.TeamOpportunity, opportunityId)));
    }

    private async Task ValidateOpportunityWriteRequestAsync(
        string? type,
        Guid sportId,
        bool canConfigureRegistration,
        bool registrationRequired,
        IReadOnlyCollection<string>? requiredRegistrationFieldCodes,
        bool waiverRequired,
        string? waiverMethod,
        bool waiverReturnByEmail,
        bool waiverReturnInPerson,
        string? contactEmail,
        int? maxParticipants,
        DateTime? registrationDeadline,
        DateTime? eventDate,
        DateTime? eventEndDate,
        DateTime? listingStartDate,
        DateTime? listingEndDate,
        DateTime? expiresAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            ModelState.AddModelError(nameof(CreateTeamOpportunityRequest.Type), "Opportunity type is required.");
        }

        var normalizedType = NormalizeOptional(type);
        var isTryoutType = string.Equals(normalizedType, "tryout", StringComparison.OrdinalIgnoreCase);
        if (registrationRequired && !isTryoutType)
        {
            ModelState.AddModelError(
                nameof(CreateTeamOpportunityRequest.RegistrationRequired),
                "Tryout registration can only be enabled for tryout listings.");
        }

        if (registrationRequired && !canConfigureRegistration)
        {
            ModelState.AddModelError(
                nameof(CreateTeamOpportunityRequest.RegistrationRequired),
                "Tryout registration requires Team Basic or higher.");
        }

        if (!registrationRequired && maxParticipants.HasValue)
        {
            ModelState.AddModelError(
                nameof(CreateTeamOpportunityRequest.MaxParticipants),
                "Max registrations can only be set when tryout registration is enabled.");
        }

        if (registrationRequired && maxParticipants is <= 0)
        {
            ModelState.AddModelError(
                nameof(CreateTeamOpportunityRequest.MaxParticipants),
                "Max registrations must be greater than zero when provided.");
        }

        var unknownFieldCodes = (requiredRegistrationFieldCodes ?? [])
            .Where(code => !TryOutSpotOpportunityRegistrationFields.IsKnownCode(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (unknownFieldCodes.Length > 0)
        {
            ModelState.AddModelError(
                nameof(CreateTeamOpportunityRequest.RequiredRegistrationFieldCodes),
                $"Unsupported registration field codes: {string.Join(", ", unknownFieldCodes)}.");
        }

        if (waiverRequired && !registrationRequired)
        {
            ModelState.AddModelError(
                nameof(CreateTeamOpportunityRequest.WaiverRequired),
                "Waiver settings can only be enabled when tryout registration is enabled.");
        }

        if (waiverRequired && !string.Equals(waiverMethod, TryOutSpotOpportunityWaiverMethods.AtEvent, StringComparison.Ordinal)
            && !string.Equals(waiverMethod, TryOutSpotOpportunityWaiverMethods.Downloadable, StringComparison.Ordinal))
        {
            ModelState.AddModelError(
                nameof(CreateTeamOpportunityRequest.WaiverMethod),
                "Choose a valid waiver method.");
        }

        var requiresWaiverReturnOption = waiverRequired
            && string.Equals(
                waiverMethod,
                TryOutSpotOpportunityWaiverMethods.Downloadable,
                StringComparison.Ordinal);
        if (requiresWaiverReturnOption && !waiverReturnByEmail && !waiverReturnInPerson)
        {
            ModelState.AddModelError(
                nameof(CreateTeamOpportunityRequest.WaiverReturnByEmail),
                "Choose at least one waiver return option (email or bring to event).");
        }

        if (waiverRequired && waiverReturnByEmail && string.IsNullOrWhiteSpace(contactEmail))
        {
            ModelState.AddModelError(
                nameof(CreateTeamOpportunityRequest.ContactEmail),
                "Contact email is required when waiver return by email is enabled.");
        }

        var sportExists = await dbContext.Sports
            .AsNoTracking()
            .AnyAsync(sport => sport.Id == sportId && sport.IsActive, cancellationToken);
        if (!sportExists)
        {
            ModelState.AddModelError(nameof(CreateTeamOpportunityRequest.SportId), "Selected sport is not available.");
        }

        var normalizedRegistrationDeadline = NormalizeUtc(registrationDeadline);
        var normalizedEventDate = NormalizeUtc(eventDate);
        var normalizedEventEndDate = NormalizeUtc(eventEndDate);
        var normalizedListingStartDate = NormalizeUtc(listingStartDate);
        var normalizedListingEndDate = NormalizeUtc(listingEndDate);
        var normalizedExpiresAt = NormalizeUtc(expiresAt);

        if (normalizedEventDate.HasValue
            && normalizedEventEndDate.HasValue
            && normalizedEventEndDate.Value < normalizedEventDate.Value)
        {
            ModelState.AddModelError(
                nameof(CreateTeamOpportunityRequest.EventEndDate),
                "Event end date cannot be earlier than event start date.");
        }

        if (normalizedRegistrationDeadline.HasValue
            && normalizedEventDate.HasValue
            && normalizedRegistrationDeadline.Value > normalizedEventDate.Value)
        {
            ModelState.AddModelError(
                nameof(CreateTeamOpportunityRequest.RegistrationDeadline),
                "Registration deadline must be on or before the event date.");
        }

        if (normalizedExpiresAt.HasValue && normalizedExpiresAt.Value <= DateTime.UtcNow)
        {
            ModelState.AddModelError(
                nameof(CreateTeamOpportunityRequest.ExpiresAt),
                "Expiration must be in the future.");
        }

        if (normalizedListingStartDate.HasValue
            && normalizedListingEndDate.HasValue
            && normalizedListingEndDate.Value < normalizedListingStartDate.Value)
        {
            ModelState.AddModelError(
                nameof(CreateTeamOpportunityRequest.ListingEndDate),
                "Listing end date cannot be earlier than listing start date.");
        }
    }

    private async Task EnsureCanPublishAsync(
        Guid teamId,
        TeamPostingAccess postingAccess,
        bool currentlyPublished,
        CancellationToken cancellationToken)
    {
        if (currentlyPublished || postingAccess.HasUnlimitedPosting)
        {
            return;
        }

        if (!postingAccess.HasLimitedPosting)
        {
            ModelState.AddModelError(
                nameof(SetTeamOpportunityPublicationRequest.IsPublished),
                "Your current membership does not include opportunity posting.");
            return;
        }

        var windowStart = DateTime.UtcNow.AddMonths(-postingAccess.PublishingWindowMonths);
        var publishedCountInWindow = await dbContext.Opportunities
            .AsNoTracking()
            .Where(opportunity => opportunity.TeamId == teamId)
            .Where(opportunity => opportunity.PublishedAt != null
                && opportunity.PublishedAt >= windowStart)
            .Where(opportunity =>
                opportunity.IsActive
                || opportunity.UpdatedAt > opportunity.PublishedAt!.Value.AddHours(24))
            .CountAsync(cancellationToken);

        if (publishedCountInWindow >= postingAccess.PublishingLimit)
        {
            ModelState.AddModelError(
                nameof(SetTeamOpportunityPublicationRequest.IsPublished),
                $"{postingAccess.PlanLabel} includes up to {postingAccess.PublishingLimit} published opportunities every {postingAccess.PublishingWindowMonths} months.");
        }
    }

    private string? ResolveZipCodeOrAddModelError(string? requestedZipCode, string? fallbackZipCode, string modelStateKey)
    {
        var normalizedRequestedZipCode = zipRadiusSearchService.NormalizeZipCode(requestedZipCode);
        if (!string.IsNullOrWhiteSpace(normalizedRequestedZipCode))
        {
            return normalizedRequestedZipCode;
        }

        var normalizedFallbackZipCode = zipRadiusSearchService.NormalizeZipCode(fallbackZipCode);
        if (!string.IsNullOrWhiteSpace(normalizedFallbackZipCode))
        {
            return normalizedFallbackZipCode;
        }

        ModelState.AddModelError(modelStateKey, "ZIP code is required and must be a valid 5-digit ZIP.");
        return null;
    }

    private async Task<ManagedTeamContext?> GetManagedTeamAsync(
        Guid userId,
        Guid teamId,
        CancellationToken cancellationToken)
    {
        var userTeamRole = await dbContext.UserTeamRoles
            .AsNoTracking()
            .Where(currentRole => currentRole.UserId == userId)
            .Where(currentRole => currentRole.TeamId == teamId)
            .Where(currentRole => currentRole.IsActive)
            .Where(currentRole => currentRole.Team.IsActive)
            .Include(currentRole => currentRole.Team)
            .SingleOrDefaultAsync(cancellationToken);

        return userTeamRole is null
            ? null
            : new ManagedTeamContext(userTeamRole.Team, userTeamRole.Role);
    }

    private async Task<TeamPostingAccess> ResolvePostingAccessAsync(Guid userId, CancellationToken cancellationToken)
    {
        var entitlements = await entitlementService.GetEntitlementsAsync(userId, cancellationToken);
        var activePlanCodes = entitlements?.ActivePlanCodes ?? [];

        if (activePlanCodes.Contains(TryOutSpotPlanCodes.EnterpriseOrganization, StringComparer.Ordinal))
        {
            return TeamPostingAccess.Enterprise;
        }

        if (activePlanCodes.Contains(TryOutSpotPlanCodes.TeamProfessional, StringComparer.Ordinal))
        {
            return TeamPostingAccess.Professional;
        }

        if (activePlanCodes.Contains(TryOutSpotPlanCodes.TeamBasic, StringComparer.Ordinal))
        {
            return TeamPostingAccess.Basic;
        }

        var featureCodes = entitlements?.FeatureCodes ?? [];
        if (!featureCodes.Contains(TryOutSpotFeatureCodes.PostLimitedOpportunities, StringComparer.Ordinal))
        {
            return TeamPostingAccess.None;
        }

        return TeamPostingAccess.FreeCoach;
    }

    private async Task<bool> CanConfigureTryoutRegistrationAsync(Guid userId, CancellationToken cancellationToken)
    {
        var entitlements = await entitlementService.GetEntitlementsAsync(userId, cancellationToken);
        var featureCodes = entitlements?.FeatureCodes ?? [];
        return featureCodes.Contains(TryOutSpotFeatureCodes.StandardRegistrationManagement, StringComparer.Ordinal)
            || featureCodes.Contains(TryOutSpotFeatureCodes.PremiumRegistrationManagement, StringComparer.Ordinal);
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdClaim, out userId);
    }

    private async Task<Dictionary<Guid, int>> GetFavoriteCountsByOpportunityIdAsync(
        IEnumerable<Guid> opportunityIds,
        CancellationToken cancellationToken)
    {
        var ids = opportunityIds.ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        return await dbContext.UserFavorites
            .AsNoTracking()
            .Where(favorite => favorite.OpportunityId.HasValue)
            .Where(favorite => ids.Contains(favorite.OpportunityId!.Value))
            .GroupBy(favorite => favorite.OpportunityId!.Value)
            .Select(group => new { OpportunityId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.OpportunityId, item => item.Count, cancellationToken);
    }

    private Task<int> GetOpportunityFavoriteCountAsync(
        Guid opportunityId,
        CancellationToken cancellationToken)
    {
        return dbContext.UserFavorites
            .AsNoTracking()
            .CountAsync(favorite => favorite.OpportunityId == opportunityId, cancellationToken);
    }

    private static ManagedTeamSummaryResponse ToManagedTeamSummary(Team team, string role)
    {
        var sports = team.TeamSports
            .Where(teamSport => teamSport.IsActive)
            .Select(teamSport => teamSport.Sport.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToArray();

        var activeOpportunities = team.Opportunities
            .Where(opportunity => opportunity.IsActive)
            .ToArray();

        return new ManagedTeamSummaryResponse(
            team.Id,
            team.Name,
            team.Organization?.Name,
            role,
            team.TeamLevel,
            team.GeographicScope,
            team.City,
            team.State,
            team.ZipCode,
            team.IsSearchable,
            team.IsContactInfoVisible,
            activeOpportunities.Length,
            activeOpportunities.Count(opportunity => opportunity.IsPublished),
            sports);
    }

    private static TeamOpportunitySummaryResponse ToSummaryResponse(Opportunity opportunity, int favoriteCount)
    {
        var requiredRegistrationFieldCodes = DeserializeRegistrationFieldCodes(opportunity.RegistrationRequiredFieldCodes);
        return new TeamOpportunitySummaryResponse(
            opportunity.Id,
            opportunity.TeamId,
            opportunity.SportId,
            opportunity.Sport.Name,
            opportunity.Type,
            opportunity.Title,
            opportunity.Description,
            opportunity.CompetitionLevel,
            opportunity.AgeGroup,
            opportunity.RegistrationRequired,
            opportunity.RegistrationFee,
            opportunity.MaxParticipants,
            requiredRegistrationFieldCodes,
            opportunity.WaiverRequired,
            TryOutSpotOpportunityWaiverMethods.Normalize(opportunity.WaiverMethod),
            opportunity.WaiverRequired && opportunity.WaiverReturnByEmail,
            opportunity.WaiverRequired && opportunity.WaiverReturnInPerson,
            opportunity.RegistrationDeadline,
            opportunity.EventDate,
            opportunity.EventEndDate,
            opportunity.ListingStartDate,
            opportunity.ListingEndDate,
            opportunity.City,
            opportunity.State,
            opportunity.ZipCode,
            opportunity.WebsiteUrl,
            opportunity.PdfUrl,
            opportunity.IsPublished,
            opportunity.PublishedAt,
            opportunity.ExpiresAt,
            favoriteCount,
            opportunity.UpdatedAt);
    }

    private static TeamOpportunityDetailResponse ToDetailResponse(Opportunity opportunity, int favoriteCount)
    {
        var requiredRegistrationFieldCodes = DeserializeRegistrationFieldCodes(opportunity.RegistrationRequiredFieldCodes);
        var waiverPdfUrl = string.IsNullOrWhiteSpace(opportunity.WaiverUploadedPdfObjectKey)
            ? null
            : $"/listing-documents/{OpportunityWaiverDocumentType}/{opportunity.Id}";
        return new TeamOpportunityDetailResponse(
            opportunity.Id,
            opportunity.TeamId,
            opportunity.SportId,
            opportunity.Sport.Name,
            opportunity.Type,
            opportunity.Title,
            opportunity.Description,
            opportunity.CompetitionLevel,
            opportunity.AgeGroup,
            opportunity.RegistrationRequired,
            opportunity.RegistrationDeadline,
            opportunity.RegistrationFee,
            opportunity.MaxParticipants,
            requiredRegistrationFieldCodes,
            opportunity.WaiverRequired,
            TryOutSpotOpportunityWaiverMethods.Normalize(opportunity.WaiverMethod),
            opportunity.WaiverRequired && opportunity.WaiverReturnByEmail,
            opportunity.WaiverRequired && opportunity.WaiverReturnInPerson,
            opportunity.EventDate,
            opportunity.EventEndDate,
            opportunity.ListingStartDate,
            opportunity.ListingEndDate,
            opportunity.Location,
            opportunity.Address,
            opportunity.City,
            opportunity.State,
            opportunity.ZipCode,
            opportunity.ContactEmail,
            opportunity.ContactPhone,
            opportunity.WebsiteUrl,
            opportunity.PdfUrl,
            waiverPdfUrl,
            opportunity.RequiredEquipment,
            opportunity.WhatToBring,
            opportunity.SpecialInstructions,
            opportunity.IsPublished,
            opportunity.PublishedAt,
            opportunity.ExpiresAt,
            favoriteCount,
            opportunity.CreatedAt,
            opportunity.UpdatedAt);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeState(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    }

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var normalized = value.Value;
        return normalized.Kind switch
        {
            DateTimeKind.Utc => normalized,
            DateTimeKind.Local => normalized.ToUniversalTime(),
            _ => DateTime.SpecifyKind(normalized, DateTimeKind.Utc)
        };
    }

    private static ListingReportSummaryResponse ToReportResponse(
        ListingReport report,
        string targetType,
        Guid targetId)
    {
        return new ListingReportSummaryResponse(
            report.Id,
            targetType,
            targetId,
            report.Reason,
            report.Details,
            report.Status,
            report.CreatedAt,
            report.UpdatedAt);
    }

    private static IReadOnlyCollection<string> NormalizeRegistrationFieldCodes(
        IReadOnlyCollection<string>? requiredRegistrationFieldCodes)
    {
        return TryOutSpotOpportunityRegistrationFields.NormalizeSelectedCodes(requiredRegistrationFieldCodes);
    }

    private static IReadOnlyCollection<string> NormalizeRequiredRegistrationFieldCodesForWaiver(
        IReadOnlyCollection<string> requiredRegistrationFieldCodes,
        bool waiverRequired)
    {
        var normalized = requiredRegistrationFieldCodes
            .Where(code => !string.Equals(code, TryOutSpotOpportunityRegistrationFields.WaiverSignature, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (waiverRequired)
        {
            normalized.Add(TryOutSpotOpportunityRegistrationFields.WaiverSignature);
        }

        return normalized.ToArray();
    }

    private static string? SerializeRegistrationFieldCodes(
        IReadOnlyCollection<string> requiredRegistrationFieldCodes)
    {
        if (requiredRegistrationFieldCodes.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(requiredRegistrationFieldCodes);
    }

    private static IReadOnlyCollection<string> DeserializeRegistrationFieldCodes(string? serializedFieldCodes)
    {
        if (string.IsNullOrWhiteSpace(serializedFieldCodes))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(serializedFieldCodes);
            return NormalizeRegistrationFieldCodes(parsed);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed record TeamPostingAccess(
        bool HasLimitedPosting,
        bool HasUnlimitedPosting,
        int PublishingLimit,
        int PublishingWindowMonths,
        string PlanLabel)
    {
        public bool CanPostOpportunities => HasLimitedPosting || HasUnlimitedPosting;

        public static TeamPostingAccess None { get; } = new(false, false, 0, 0, "No plan");
        public static TeamPostingAccess Professional { get; } = new(true, false, ProfessionalTeamPublishingLimit, ProfessionalTeamPublishingWindowMonths, "Professional Team");
        public static TeamPostingAccess Enterprise { get; } = new(true, false, EnterpriseTeamPublishingLimit, EnterpriseTeamPublishingWindowMonths, "Enterprise Organization");
        public static TeamPostingAccess Basic { get; } = new(true, false, BasicTeamPublishingLimit, BasicTeamPublishingWindowMonths, "Basic Team");
        public static TeamPostingAccess FreeCoach { get; } = new(true, false, FreeCoachPublishingLimit, FreeCoachPublishingWindowMonths, "Free Coach");
    }

    private sealed record ManagedTeamContext(Team Team, string Role);
}
