using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Models.Discovery;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Controllers;

/// <summary>
/// Public discovery endpoints for teams, organizations, and opportunities.
/// </summary>
[ApiController]
[Tags("Discovery")]
[Produces("application/json")]
[Route("api/discovery")]
public sealed class DiscoveryApiController(
    AppDbContext dbContext,
    IEntitlementService entitlementService,
    IZipRadiusSearchService zipRadiusSearchService) : ControllerBase
{
    private const int FreeOpportunitySearchMaxRadiusMiles = 120;

    /// <summary>
    /// Searches public team listings.
    /// </summary>
    [HttpGet("teams")]
    [ProducesResponseType<TeamDiscoveryListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TeamDiscoveryListResponse>> SearchTeams(
        [FromQuery] string? q,
        [FromQuery] Guid? sportId,
        [FromQuery] Guid? organizationId,
        [FromQuery] string? teamLevel,
        [FromQuery] string? geographicScope,
        [FromQuery] string? zipCode,
        [FromQuery] string? originZipCode,
        [FromQuery] int? radiusMiles,
        [FromQuery] string? city,
        [FromQuery] string? state,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var zipRadius = await ResolveZipRadiusAsync(originZipCode, radiusMiles, hasAdvancedOpportunitySearch: true, cancellationToken);
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.Teams
            .AsNoTracking()
            .Where(team => team.IsActive)
            .Where(team => team.IsSearchable);

        if (sportId.HasValue)
        {
            query = query.Where(team =>
                team.TeamSports.Any(teamSport => teamSport.IsActive && teamSport.SportId == sportId.Value));
        }

        if (organizationId.HasValue)
        {
            query = query.Where(team => team.OrganizationId == organizationId.Value);
        }

        var normalizedTeamLevel = NormalizeOptional(teamLevel);
        if (!string.IsNullOrWhiteSpace(normalizedTeamLevel))
        {
            var teamLevelSearch = normalizedTeamLevel.ToLowerInvariant();
            query = query.Where(team => team.TeamLevel != null && team.TeamLevel.ToLower().Contains(teamLevelSearch));
        }

        var normalizedGeographicScope = NormalizeOptional(geographicScope);
        if (!string.IsNullOrWhiteSpace(normalizedGeographicScope))
        {
            var geographicScopeSearch = normalizedGeographicScope.ToLowerInvariant();
            query = query.Where(team =>
                team.GeographicScope != null
                && team.GeographicScope.ToLower().Contains(geographicScopeSearch));
        }

        var normalizedZipCode = zipRadiusSearchService.NormalizeZipCode(zipCode);
        if (!string.IsNullOrWhiteSpace(normalizedZipCode))
        {
            query = query.Where(team => team.ZipCode == normalizedZipCode);
        }

        if (zipRadius is not null)
        {
            var radiusZipCodes = zipRadius.ZipCodes;
            query = query.Where(team =>
                team.ZipCode != null
                && radiusZipCodes.Contains(team.ZipCode));
        }

        var normalizedCity = NormalizeOptional(city);
        if (!string.IsNullOrWhiteSpace(normalizedCity))
        {
            var citySearch = normalizedCity.ToLowerInvariant();
            query = query.Where(team => team.City != null && team.City.ToLower().Contains(citySearch));
        }

        var normalizedState = NormalizeState(state);
        if (!string.IsNullOrWhiteSpace(normalizedState))
        {
            query = query.Where(team => team.State == normalizedState);
        }

        var normalizedSearch = NormalizeOptional(q);
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var search = normalizedSearch.ToLowerInvariant();
            query = query.Where(team =>
                team.Name.ToLower().Contains(search)
                || (team.Description != null && team.Description.ToLower().Contains(search))
                || (team.City != null && team.City.ToLower().Contains(search))
                || (team.State != null && team.State.ToLower().Contains(search))
                || (team.ZipCode != null && team.ZipCode.Contains(search))
                || (team.Organization != null && team.Organization.Name.ToLower().Contains(search)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var teams = await query
            .Include(team => team.Organization)
            .Include(team => team.TeamSports)
                .ThenInclude(teamSport => teamSport.Sport)
            .OrderByDescending(team => team.IsVerified)
            .ThenBy(team => team.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        var distanceByZipCode = zipRadius?.DistanceByZipCode;
        var teamResponses = teams
            .Select(team => ToTeamResponse(team, distanceByZipCode))
            .ToArray();

        return Ok(new TeamDiscoveryListResponse(
            teamResponses,
            page,
            pageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)pageSize),
            zipRadius?.OriginZipCode,
            zipRadius?.RadiusMiles));
    }

    /// <summary>
    /// Searches public organization listings.
    /// </summary>
    [HttpGet("organizations")]
    [ProducesResponseType<OrganizationDiscoveryListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrganizationDiscoveryListResponse>> SearchOrganizations(
        [FromQuery] string? q,
        [FromQuery] Guid? sportId,
        [FromQuery] bool? isAcademy,
        [FromQuery] string? zipCode,
        [FromQuery] string? originZipCode,
        [FromQuery] int? radiusMiles,
        [FromQuery] string? city,
        [FromQuery] string? state,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var zipRadius = await ResolveZipRadiusAsync(originZipCode, radiusMiles, hasAdvancedOpportunitySearch: true, cancellationToken);
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.Organizations
            .AsNoTracking()
            .Where(organization => organization.IsActive)
            .Where(organization => organization.IsSearchable);

        if (sportId.HasValue)
        {
            query = query.Where(organization =>
                organization.Teams.Any(team =>
                    team.IsActive
                    && team.TeamSports.Any(teamSport => teamSport.IsActive && teamSport.SportId == sportId.Value)));
        }

        if (isAcademy.HasValue)
        {
            query = query.Where(organization => organization.IsAcademy == isAcademy.Value);
        }

        var normalizedZipCode = zipRadiusSearchService.NormalizeZipCode(zipCode);
        if (!string.IsNullOrWhiteSpace(normalizedZipCode))
        {
            query = query.Where(organization => organization.ZipCode == normalizedZipCode);
        }

        if (zipRadius is not null)
        {
            var radiusZipCodes = zipRadius.ZipCodes;
            query = query.Where(organization =>
                organization.ZipCode != null
                && radiusZipCodes.Contains(organization.ZipCode));
        }

        var normalizedCity = NormalizeOptional(city);
        if (!string.IsNullOrWhiteSpace(normalizedCity))
        {
            var citySearch = normalizedCity.ToLowerInvariant();
            query = query.Where(organization =>
                organization.City != null
                && organization.City.ToLower().Contains(citySearch));
        }

        var normalizedState = NormalizeState(state);
        if (!string.IsNullOrWhiteSpace(normalizedState))
        {
            query = query.Where(organization => organization.State == normalizedState);
        }

        var normalizedSearch = NormalizeOptional(q);
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var search = normalizedSearch.ToLowerInvariant();
            query = query.Where(organization =>
                organization.Name.ToLower().Contains(search)
                || (organization.Description != null && organization.Description.ToLower().Contains(search))
                || (organization.City != null && organization.City.ToLower().Contains(search))
                || (organization.State != null && organization.State.ToLower().Contains(search))
                || (organization.ZipCode != null && organization.ZipCode.Contains(search)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var organizations = await query
            .Include(organization => organization.Teams)
                .ThenInclude(team => team.TeamSports)
                    .ThenInclude(teamSport => teamSport.Sport)
            .OrderByDescending(organization => organization.IsVerified)
            .ThenBy(organization => organization.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        var distanceByZipCode = zipRadius?.DistanceByZipCode;
        var organizationResponses = organizations
            .Select(organization => ToOrganizationResponse(organization, distanceByZipCode))
            .ToArray();

        return Ok(new OrganizationDiscoveryListResponse(
            organizationResponses,
            page,
            pageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)pageSize),
            zipRadius?.OriginZipCode,
            zipRadius?.RadiusMiles));
    }

    /// <summary>
    /// Searches published opportunities.
    /// </summary>
    [HttpGet("opportunities")]
    [ProducesResponseType<OpportunityDiscoveryListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OpportunityDiscoveryListResponse>> SearchOpportunities(
        [FromQuery] string? q,
        [FromQuery] Guid? sportId,
        [FromQuery] Guid? teamId,
        [FromQuery] string? type,
        [FromQuery] string? ageGroup,
        [FromQuery] DateTime? eventDateFrom,
        [FromQuery] DateTime? eventDateTo,
        [FromQuery] string? zipCode,
        [FromQuery] string? originZipCode,
        [FromQuery] int? radiusMiles,
        [FromQuery] string? city,
        [FromQuery] string? state,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (eventDateFrom.HasValue && eventDateTo.HasValue && eventDateFrom.Value > eventDateTo.Value)
        {
            ModelState.AddModelError(nameof(eventDateFrom), "Event start date cannot be after event end date.");
        }

        var normalizedEventDateFrom = NormalizeUtc(eventDateFrom);
        var normalizedEventDateTo = NormalizeUtc(eventDateTo);
        var hasAdvancedOpportunitySearch = await HasAdvancedOpportunitySearchAsync(cancellationToken);
        var zipRadius = await ResolveZipRadiusAsync(
            originZipCode,
            radiusMiles,
            hasAdvancedOpportunitySearch,
            cancellationToken);
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var now = DateTime.UtcNow;
        var query = dbContext.Opportunities
            .AsNoTracking()
            .Where(opportunity => opportunity.IsActive)
            .Where(opportunity => opportunity.IsPublished)
            .Where(opportunity =>
                (opportunity.ListingEndDate ?? opportunity.ExpiresAt) == null
                || (opportunity.ListingEndDate ?? opportunity.ExpiresAt) > now)
            .Where(opportunity => opportunity.Team.IsActive)
            .Where(opportunity => opportunity.Team.IsSearchable);

        if (!hasAdvancedOpportunitySearch)
        {
            query = query.Where(opportunity => opportunity.ListingStartDate == null || opportunity.ListingStartDate <= now);
        }

        if (sportId.HasValue)
        {
            query = query.Where(opportunity => opportunity.SportId == sportId.Value);
        }

        if (teamId.HasValue)
        {
            query = query.Where(opportunity => opportunity.TeamId == teamId.Value);
        }

        if (hasAdvancedOpportunitySearch)
        {
            var normalizedType = NormalizeOptional(type);
            if (!string.IsNullOrWhiteSpace(normalizedType))
            {
                var typeSearch = normalizedType.ToLowerInvariant();
                query = query.Where(opportunity =>
                    opportunity.Type != null
                    && opportunity.Type.ToLower().Contains(typeSearch));
            }
        }
        else
        {
            // Free discovery is constrained to tryout opportunities.
            query = query.Where(opportunity =>
                opportunity.Type != null
                && opportunity.Type.ToLower().Contains("tryout"));
        }

        var normalizedAgeGroup = NormalizeOptional(ageGroup);
        if (!string.IsNullOrWhiteSpace(normalizedAgeGroup))
        {
            var ageGroupSearch = normalizedAgeGroup.ToLowerInvariant();
            query = query.Where(opportunity =>
                opportunity.AgeGroup != null
                && opportunity.AgeGroup.ToLower().Contains(ageGroupSearch));
        }

        if (normalizedEventDateFrom.HasValue)
        {
            query = query.Where(opportunity =>
                opportunity.EventDate != null
                && opportunity.EventDate >= normalizedEventDateFrom.Value);
        }

        if (normalizedEventDateTo.HasValue)
        {
            query = query.Where(opportunity =>
                opportunity.EventDate != null
                && opportunity.EventDate <= normalizedEventDateTo.Value);
        }

        var normalizedZipCode = zipRadiusSearchService.NormalizeZipCode(zipCode);
        if (!string.IsNullOrWhiteSpace(normalizedZipCode))
        {
            query = query.Where(opportunity =>
                opportunity.ZipCode == normalizedZipCode
                || (opportunity.ZipCode == null && opportunity.Team.ZipCode == normalizedZipCode));
        }

        if (zipRadius is not null)
        {
            var radiusZipCodes = zipRadius.ZipCodes;
            query = query.Where(opportunity =>
                (opportunity.ZipCode != null && radiusZipCodes.Contains(opportunity.ZipCode))
                || (opportunity.ZipCode == null
                    && opportunity.Team.ZipCode != null
                    && radiusZipCodes.Contains(opportunity.Team.ZipCode)));
        }

        var normalizedCity = NormalizeOptional(city);
        if (!string.IsNullOrWhiteSpace(normalizedCity))
        {
            var citySearch = normalizedCity.ToLowerInvariant();
            query = query.Where(opportunity =>
                (opportunity.City != null && opportunity.City.ToLower().Contains(citySearch))
                || (opportunity.City == null && opportunity.Team.City != null && opportunity.Team.City.ToLower().Contains(citySearch)));
        }

        var normalizedState = NormalizeState(state);
        if (!string.IsNullOrWhiteSpace(normalizedState))
        {
            query = query.Where(opportunity =>
                opportunity.State == normalizedState
                || (opportunity.State == null && opportunity.Team.State == normalizedState));
        }

        var normalizedSearch = NormalizeOptional(q);
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var search = normalizedSearch.ToLowerInvariant();
            query = query.Where(opportunity =>
                opportunity.Title.ToLower().Contains(search)
                || (opportunity.Description != null && opportunity.Description.ToLower().Contains(search))
                || (opportunity.City != null && opportunity.City.ToLower().Contains(search))
                || (opportunity.State != null && opportunity.State.ToLower().Contains(search))
                || (opportunity.ZipCode != null && opportunity.ZipCode.Contains(search))
                || opportunity.Team.Name.ToLower().Contains(search)
                || (opportunity.Team.Organization != null && opportunity.Team.Organization.Name.ToLower().Contains(search)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var opportunities = await query
            .Include(opportunity => opportunity.Sport)
            .Include(opportunity => opportunity.Team)
                .ThenInclude(team => team.Organization)
            .OrderBy(opportunity => opportunity.EventDate ?? DateTime.MaxValue)
            .ThenByDescending(opportunity => opportunity.PublishedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        var distanceByZipCode = zipRadius?.DistanceByZipCode;
        var favoriteOpportunityIds = await GetFavoriteOpportunityIdsAsync(opportunities, cancellationToken);
        var opportunityResponses = opportunities
            .Select(opportunity => ToOpportunityResponse(opportunity, distanceByZipCode, favoriteOpportunityIds))
            .ToArray();

        return Ok(new OpportunityDiscoveryListResponse(
            opportunityResponses,
            page,
            pageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)pageSize),
            zipRadius?.OriginZipCode,
            zipRadius?.RadiusMiles,
            hasAdvancedOpportunitySearch));
    }

    private async Task<ZipRadiusSearchResult?> ResolveZipRadiusAsync(
        string? originZipCode,
        int? radiusMiles,
        bool hasAdvancedOpportunitySearch,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(originZipCode))
        {
            if (radiusMiles.HasValue)
            {
                ModelState.AddModelError(
                    nameof(originZipCode),
                    "Origin ZIP code is required when radius is provided.");
            }

            return null;
        }

        var normalizedOriginZipCode = zipRadiusSearchService.NormalizeZipCode(originZipCode);
        if (normalizedOriginZipCode is null)
        {
            ModelState.AddModelError(nameof(originZipCode), "Enter a valid 5-digit ZIP code.");
            return null;
        }

        var normalizedRadiusMiles = zipRadiusSearchService.ClampRadiusMiles(radiusMiles);
        if (!hasAdvancedOpportunitySearch)
        {
            normalizedRadiusMiles = Math.Min(normalizedRadiusMiles, FreeOpportunitySearchMaxRadiusMiles);
        }

        var zipRadiusResult = await zipRadiusSearchService.ResolveZipCodesWithinRadiusAsync(
            normalizedOriginZipCode,
            normalizedRadiusMiles,
            cancellationToken);
        if (zipRadiusResult is null)
        {
            ModelState.AddModelError(
                nameof(originZipCode),
                "That ZIP code is not in the geographic catalog yet.");
        }

        return zipRadiusResult;
    }

    private async Task<bool> HasAdvancedOpportunitySearchAsync(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return false;
        }

        return await entitlementService.HasFeatureAsync(
            userId,
            TryOutSpotFeatureCodes.AdvancedOpportunitySearch,
            cancellationToken);
    }

    private async Task<IReadOnlySet<Guid>> GetFavoriteOpportunityIdsAsync(
        IReadOnlyCollection<Data.Entities.Opportunity> opportunities,
        CancellationToken cancellationToken)
    {
        if (opportunities.Count == 0 || !TryGetCurrentUserId(out var userId))
        {
            return new HashSet<Guid>();
        }

        var opportunityIds = opportunities
            .Select(opportunity => opportunity.Id)
            .ToArray();

        return (await dbContext.UserFavorites
                .AsNoTracking()
                .Where(favorite => favorite.UserId == userId)
                .Where(favorite => favorite.OpportunityId != null)
                .Where(favorite => opportunityIds.Contains(favorite.OpportunityId!.Value))
                .Select(favorite => favorite.OpportunityId!.Value)
                .ToArrayAsync(cancellationToken))
            .ToHashSet();
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdClaim, out userId);
    }

    private static TeamDiscoverySummaryResponse ToTeamResponse(
        Data.Entities.Team team,
        IReadOnlyDictionary<string, double>? distanceByZipCode)
    {
        var websiteUrl = team.IsContactInfoVisible ? team.WebsiteUrl : null;
        var contactEmail = team.IsContactInfoVisible ? team.Email : null;
        var contactPhone = team.IsContactInfoVisible ? team.PhoneNumber : null;
        var socialMediaLinks = team.IsContactInfoVisible ? team.SocialMediaLinks : null;
        var distanceMiles = ResolveDistanceMiles(team.ZipCode, distanceByZipCode);

        var sports = team.TeamSports
            .Where(teamSport => teamSport.IsActive)
            .Select(teamSport => teamSport.Sport.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToArray();

        return new TeamDiscoverySummaryResponse(
            team.Id,
            team.Name,
            team.OrganizationId,
            team.Organization?.Name,
            ResolveTeamLogoPublicUrl(team.Id, team.LogoImageUrl),
            team.TeamLevel,
            team.GeographicScope,
            sports,
            team.City,
            team.State,
            team.ZipCode,
            team.IsVerified,
            team.IsContactInfoVisible,
            websiteUrl,
            contactEmail,
            contactPhone,
            socialMediaLinks,
            distanceMiles);
    }

    private static OrganizationDiscoverySummaryResponse ToOrganizationResponse(
        Data.Entities.Organization organization,
        IReadOnlyDictionary<string, double>? distanceByZipCode)
    {
        var websiteUrl = organization.IsContactInfoVisible ? organization.WebsiteUrl : null;
        var contactEmail = organization.IsContactInfoVisible ? organization.Email : null;
        var contactPhone = organization.IsContactInfoVisible ? organization.PhoneNumber : null;
        var socialMediaLinks = organization.IsContactInfoVisible ? organization.SocialMediaLinks : null;
        var distanceMiles = ResolveDistanceMiles(organization.ZipCode, distanceByZipCode);

        var activeTeams = organization.Teams.Where(team => team.IsActive).ToArray();
        var sports = activeTeams
            .SelectMany(team => team.TeamSports)
            .Where(teamSport => teamSport.IsActive)
            .Select(teamSport => teamSport.Sport.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name)
            .ToArray();

        return new OrganizationDiscoverySummaryResponse(
            organization.Id,
            organization.Name,
            organization.IsAcademy,
            sports,
            activeTeams.Length,
            organization.City,
            organization.State,
            organization.ZipCode,
            organization.IsVerified,
            organization.IsContactInfoVisible,
            websiteUrl,
            contactEmail,
            contactPhone,
            socialMediaLinks,
            distanceMiles);
    }

    private static OpportunityDiscoverySummaryResponse ToOpportunityResponse(
        Data.Entities.Opportunity opportunity,
        IReadOnlyDictionary<string, double>? distanceByZipCode,
        IReadOnlySet<Guid>? favoriteOpportunityIds = null)
    {
        var isContactInfoVisible = opportunity.Team.IsContactInfoVisible
            && (opportunity.Team.Organization?.IsContactInfoVisible ?? true);

        var contactEmail = isContactInfoVisible ? opportunity.ContactEmail : null;
        var contactPhone = isContactInfoVisible ? opportunity.ContactPhone : null;
        var websiteUrl = isContactInfoVisible ? opportunity.WebsiteUrl : null;
        var pdfUrl = isContactInfoVisible
            ? string.IsNullOrWhiteSpace(opportunity.UploadedPdfObjectKey)
                ? opportunity.PdfUrl
                : $"/listing-documents/opportunities/{opportunity.Id}"
            : null;
        var effectiveZipCode = opportunity.ZipCode ?? opportunity.Team.ZipCode;
        var distanceMiles = ResolveDistanceMiles(effectiveZipCode, distanceByZipCode);

        return new OpportunityDiscoverySummaryResponse(
            opportunity.Id,
            opportunity.TeamId,
            opportunity.Team.Name,
            ResolveTeamLogoPublicUrl(opportunity.TeamId, opportunity.Team.LogoImageUrl),
            opportunity.Team.OrganizationId,
            opportunity.Team.Organization?.Name,
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
            opportunity.City ?? opportunity.Team.City,
            opportunity.State ?? opportunity.Team.State,
            effectiveZipCode,
            isContactInfoVisible,
            contactEmail,
            contactPhone,
            websiteUrl,
            pdfUrl,
            distanceMiles,
            IsFavorited: favoriteOpportunityIds?.Contains(opportunity.Id) == true);
    }

    private static double? ResolveDistanceMiles(
        string? zipCode,
        IReadOnlyDictionary<string, double>? distanceByZipCode)
    {
        if (distanceByZipCode is null || string.IsNullOrWhiteSpace(zipCode))
        {
            return null;
        }

        return distanceByZipCode.TryGetValue(zipCode, out var distanceMiles)
            ? Math.Round(distanceMiles, 1)
            : null;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? ResolveTeamLogoPublicUrl(Guid teamId, string? logoValue)
    {
        var normalizedLogo = NormalizeOptional(logoValue);
        if (normalizedLogo is null)
        {
            return null;
        }

        return normalizedLogo.StartsWith("r2:", StringComparison.Ordinal)
            ? $"/media/team-logos/{teamId}"
            : normalizedLogo;
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
}
