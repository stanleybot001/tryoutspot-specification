using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
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
    private const int BasicTeamMonthlyPublishingLimit = 5;

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

        return Ok(new TeamOpportunityListResponse(
            teamId,
            opportunities.Select(ToSummaryResponse).ToArray(),
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

        return opportunity is null ? NotFound() : Ok(ToDetailResponse(opportunity));
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

        await ValidateOpportunityWriteRequestAsync(
            request.Type,
            request.SportId,
            request.RegistrationDeadline,
            request.EventDate,
            request.EventEndDate,
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
            RegistrationRequired = request.RegistrationRequired,
            RegistrationDeadline = NormalizeUtc(request.RegistrationDeadline),
            RegistrationFee = request.RegistrationFee,
            EventDate = NormalizeUtc(request.EventDate),
            EventEndDate = NormalizeUtc(request.EventEndDate),
            Location = NormalizeOptional(request.Location),
            Address = NormalizeOptional(request.Address),
            City = NormalizeOptional(request.City) ?? managedTeam.Team.City,
            State = NormalizeState(request.State) ?? managedTeam.Team.State,
            ZipCode = normalizedZipCode,
            ContactEmail = NormalizeOptional(request.ContactEmail) ?? managedTeam.Team.Email,
            ContactPhone = NormalizeOptional(request.ContactPhone) ?? managedTeam.Team.PhoneNumber,
            WebsiteUrl = NormalizeOptional(request.WebsiteUrl) ?? managedTeam.Team.WebsiteUrl,
            RequiredEquipment = NormalizeOptional(request.RequiredEquipment),
            WhatToBring = NormalizeOptional(request.WhatToBring),
            SpecialInstructions = NormalizeOptional(request.SpecialInstructions),
            IsPublished = request.IsPublished,
            PublishedAt = request.IsPublished ? now : null,
            ExpiresAt = NormalizeUtc(request.ExpiresAt),
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
            new TeamOpportunityActionResponse("Opportunity created.", ToDetailResponse(created)));
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
            request.RegistrationDeadline,
            request.EventDate,
            request.EventEndDate,
            request.ExpiresAt,
            cancellationToken);

        var normalizedZipCode = ResolveZipCodeOrAddModelError(request.ZipCode, managedTeam.Team.ZipCode, nameof(request.ZipCode));
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        opportunity.SportId = request.SportId;
        opportunity.Type = request.Type.Trim();
        opportunity.Title = request.Title.Trim();
        opportunity.Description = NormalizeOptional(request.Description);
        opportunity.CompetitionLevel = NormalizeOptional(request.CompetitionLevel);
        opportunity.AgeGroup = NormalizeOptional(request.AgeGroup);
        opportunity.RegistrationRequired = request.RegistrationRequired;
        opportunity.RegistrationDeadline = NormalizeUtc(request.RegistrationDeadline);
        opportunity.RegistrationFee = request.RegistrationFee;
        opportunity.EventDate = NormalizeUtc(request.EventDate);
        opportunity.EventEndDate = NormalizeUtc(request.EventEndDate);
        opportunity.Location = NormalizeOptional(request.Location);
        opportunity.Address = NormalizeOptional(request.Address);
        opportunity.City = NormalizeOptional(request.City) ?? managedTeam.Team.City;
        opportunity.State = NormalizeState(request.State) ?? managedTeam.Team.State;
        opportunity.ZipCode = normalizedZipCode;
        opportunity.ContactEmail = NormalizeOptional(request.ContactEmail) ?? managedTeam.Team.Email;
        opportunity.ContactPhone = NormalizeOptional(request.ContactPhone) ?? managedTeam.Team.PhoneNumber;
        opportunity.WebsiteUrl = NormalizeOptional(request.WebsiteUrl) ?? managedTeam.Team.WebsiteUrl;
        opportunity.RequiredEquipment = NormalizeOptional(request.RequiredEquipment);
        opportunity.WhatToBring = NormalizeOptional(request.WhatToBring);
        opportunity.SpecialInstructions = NormalizeOptional(request.SpecialInstructions);
        opportunity.ExpiresAt = NormalizeUtc(request.ExpiresAt);
        opportunity.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        var updated = await dbContext.Opportunities
            .AsNoTracking()
            .Where(currentOpportunity => currentOpportunity.Id == opportunityId)
            .Include(currentOpportunity => currentOpportunity.Sport)
            .SingleAsync(cancellationToken);

        return Ok(new TeamOpportunityActionResponse("Opportunity updated.", ToDetailResponse(updated)));
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
            : null;
        opportunity.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        var updated = await dbContext.Opportunities
            .AsNoTracking()
            .Where(currentOpportunity => currentOpportunity.Id == opportunityId)
            .Include(currentOpportunity => currentOpportunity.Sport)
            .SingleAsync(cancellationToken);

        return Ok(new TeamOpportunityActionResponse(
            request.IsPublished ? "Opportunity published." : "Opportunity unpublished.",
            ToDetailResponse(updated)));
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
        opportunity.PublishedAt = null;
        opportunity.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var updated = await dbContext.Opportunities
            .AsNoTracking()
            .Where(currentOpportunity => currentOpportunity.Id == opportunityId)
            .Include(currentOpportunity => currentOpportunity.Sport)
            .SingleAsync(cancellationToken);

        return Ok(new TeamOpportunityActionResponse("Opportunity deactivated.", ToDetailResponse(updated)));
    }

    private async Task ValidateOpportunityWriteRequestAsync(
        string? type,
        Guid sportId,
        DateTime? registrationDeadline,
        DateTime? eventDate,
        DateTime? eventEndDate,
        DateTime? expiresAt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            ModelState.AddModelError(nameof(CreateTeamOpportunityRequest.Type), "Opportunity type is required.");
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

        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var monthEnd = monthStart.AddMonths(1);
        var publishedCountThisMonth = await dbContext.Opportunities
            .AsNoTracking()
            .Where(opportunity => opportunity.TeamId == teamId)
            .Where(opportunity => opportunity.IsActive)
            .Where(opportunity => opportunity.IsPublished)
            .Where(opportunity => opportunity.PublishedAt != null
                && opportunity.PublishedAt >= monthStart
                && opportunity.PublishedAt < monthEnd)
            .CountAsync(cancellationToken);

        if (publishedCountThisMonth >= BasicTeamMonthlyPublishingLimit)
        {
            ModelState.AddModelError(
                nameof(SetTeamOpportunityPublicationRequest.IsPublished),
                $"Basic Team includes up to {BasicTeamMonthlyPublishingLimit} published opportunities per month. Upgrade to Professional Team for unlimited postings.");
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
        var featureCodes = entitlements?.FeatureCodes ?? [];

        return new TeamPostingAccess(
            featureCodes.Contains(TryOutSpotFeatureCodes.PostLimitedOpportunities, StringComparer.Ordinal),
            featureCodes.Contains(TryOutSpotFeatureCodes.UnlimitedOpportunityPostings, StringComparer.Ordinal));
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdClaim, out userId);
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

    private static TeamOpportunitySummaryResponse ToSummaryResponse(Opportunity opportunity)
    {
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
            opportunity.RegistrationFee,
            opportunity.RegistrationDeadline,
            opportunity.EventDate,
            opportunity.EventEndDate,
            opportunity.City,
            opportunity.State,
            opportunity.ZipCode,
            opportunity.IsPublished,
            opportunity.PublishedAt,
            opportunity.ExpiresAt,
            opportunity.UpdatedAt);
    }

    private static TeamOpportunityDetailResponse ToDetailResponse(Opportunity opportunity)
    {
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
            opportunity.EventDate,
            opportunity.EventEndDate,
            opportunity.Location,
            opportunity.Address,
            opportunity.City,
            opportunity.State,
            opportunity.ZipCode,
            opportunity.ContactEmail,
            opportunity.ContactPhone,
            opportunity.WebsiteUrl,
            opportunity.RequiredEquipment,
            opportunity.WhatToBring,
            opportunity.SpecialInstructions,
            opportunity.IsPublished,
            opportunity.PublishedAt,
            opportunity.ExpiresAt,
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

    private sealed record TeamPostingAccess(bool HasLimitedPosting, bool HasUnlimitedPosting)
    {
        public bool CanPostOpportunities => HasLimitedPosting || HasUnlimitedPosting;
    }

    private sealed record ManagedTeamContext(Team Team, string Role);
}
