using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Listings;
using TryOutSpot.Web.Models.Listings;
using TryOutSpot.Web.Security;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Controllers;

/// <summary>
/// Parent and player listing endpoints for discovery-focused classifieds.
/// </summary>
[ApiController]
[Tags("Player Listings")]
[Produces("application/json")]
[Route("api/player-listings")]
[Authorize(Policy = TryOutSpotAuthorizationPolicies.ActiveUser)]
public sealed class PlayerListingsApiController(
    AppDbContext dbContext,
    IEntitlementService entitlementService,
    IZipRadiusSearchService zipRadiusSearchService) : ControllerBase
{
    private const string CreateListingsPolicy =
        TryOutSpotAuthorizationPolicies.FeaturePolicyPrefix + TryOutSpotFeatureCodes.CreatePlayerListings;
    private const int FreeCoachMaxPlayerSearchRadiusMiles = 120;

    /// <summary>
    /// Searches published and searchable parent/player listings.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("search")]
    [ProducesResponseType<PlayerListingListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PlayerListingListResponse>> Search(
        [FromQuery] string? listingType,
        [FromQuery] Guid? sportId,
        [FromQuery] int? minAge,
        [FromQuery] int? maxAge,
        [FromQuery] string? skillLevel,
        [FromQuery] string? zipCode,
        [FromQuery] string? originZipCode,
        [FromQuery] int? radiusMiles,
        [FromQuery] string? city,
        [FromQuery] string? state,
        [FromQuery] string? q,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (minPrice.HasValue && minPrice.Value < 0m)
        {
            ModelState.AddModelError(nameof(minPrice), "Minimum price cannot be negative.");
        }

        if (maxPrice.HasValue && maxPrice.Value < 0m)
        {
            ModelState.AddModelError(nameof(maxPrice), "Maximum price cannot be negative.");
        }

        if (minPrice.HasValue && maxPrice.HasValue && minPrice.Value > maxPrice.Value)
        {
            ModelState.AddModelError(nameof(minPrice), "Minimum price cannot exceed maximum price.");
        }

        if (minAge.HasValue && minAge.Value < 0)
        {
            ModelState.AddModelError(nameof(minAge), "Minimum age cannot be negative.");
        }

        if (maxAge.HasValue && maxAge.Value < 0)
        {
            ModelState.AddModelError(nameof(maxAge), "Maximum age cannot be negative.");
        }

        if (minAge.HasValue && maxAge.HasValue && minAge.Value > maxAge.Value)
        {
            ModelState.AddModelError(nameof(minAge), "Minimum age cannot exceed maximum age.");
        }

        if (radiusMiles.HasValue && string.IsNullOrWhiteSpace(originZipCode))
        {
            ModelState.AddModelError(nameof(originZipCode), "Origin ZIP code is required when radius is provided.");
        }

        var hasAdvancedPlayerSearch = await HasAdvancedPlayerSearchAsync(cancellationToken);
        var isConstrainedTeamSearch = IsSignedInTeamRepresentative() && !hasAdvancedPlayerSearch;

        var normalizedSkillLevel = NormalizeOptional(skillLevel);
        if (isConstrainedTeamSearch && !string.IsNullOrWhiteSpace(normalizedSkillLevel))
        {
            ModelState.AddModelError(
                nameof(skillLevel),
                "Skill-level filtering requires Team Basic, Team Professional, or Enterprise.");
        }

        string? normalizedListingType = null;
        if (!string.IsNullOrWhiteSpace(listingType))
        {
            normalizedListingType = NormalizeListingType(listingType, nameof(listingType));
        }

        string? normalizedOriginZipCode = null;
        ZipRadiusSearchResult? zipRadiusResult = null;
        if (!string.IsNullOrWhiteSpace(originZipCode))
        {
            normalizedOriginZipCode = zipRadiusSearchService.NormalizeZipCode(originZipCode);
            if (normalizedOriginZipCode is null)
            {
                ModelState.AddModelError(nameof(originZipCode), "Enter a valid 5-digit ZIP code.");
            }
            else
            {
                var normalizedRadiusMiles = zipRadiusSearchService.ClampRadiusMiles(radiusMiles);
                if (isConstrainedTeamSearch)
                {
                    normalizedRadiusMiles = Math.Min(normalizedRadiusMiles, FreeCoachMaxPlayerSearchRadiusMiles);
                }

                zipRadiusResult = await zipRadiusSearchService.ResolveZipCodesWithinRadiusAsync(
                    normalizedOriginZipCode,
                    normalizedRadiusMiles,
                    cancellationToken);

                if (zipRadiusResult is null)
                {
                    ModelState.AddModelError(
                        nameof(originZipCode),
                        "That ZIP code is not in the geographic catalog yet.");
                }
                else if (zipRadiusResult.ZipCodes.Count == 0)
                {
                    return Ok(new PlayerListingListResponse(
                        [],
                        Page: 1,
                        PageSize: Math.Clamp(pageSize, 1, 100),
                        TotalCount: 0,
                        TotalPages: 0,
                        SearchOriginZipCode: zipRadiusResult.OriginZipCode,
                        SearchRadiusMiles: zipRadiusResult.RadiusMiles,
                        AdvancedFiltersApplied: hasAdvancedPlayerSearch));
                }
            }
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var now = DateTime.UtcNow;

        var query = dbContext.PlayerListings
            .AsNoTracking()
            .Where(listing => listing.IsActive)
            .Where(listing => listing.IsPublished)
            .Where(listing => listing.IsSearchable)
            .Where(listing => listing.ExpiresAt == null || listing.ExpiresAt > now);

        if (normalizedListingType is not null)
        {
            query = query.Where(listing => listing.ListingType == normalizedListingType);
        }

        if (sportId.HasValue)
        {
            query = query.Where(listing => listing.SportId == sportId.Value);
        }

        if (minAge.HasValue || maxAge.HasValue)
        {
            var today = DateTime.UtcNow.Date;
            if (minAge.HasValue)
            {
                var maxDobForMinAge = today.AddYears(-minAge.Value);
                query = query.Where(listing =>
                    listing.Player != null
                    && listing.Player.DateOfBirth <= maxDobForMinAge);
            }

            if (maxAge.HasValue)
            {
                var minDobForMaxAge = today.AddYears(-(maxAge.Value + 1)).AddDays(1);
                query = query.Where(listing =>
                    listing.Player != null
                    && listing.Player.DateOfBirth >= minDobForMaxAge);
            }
        }

        if (!string.IsNullOrWhiteSpace(normalizedSkillLevel) && hasAdvancedPlayerSearch)
        {
            var skillLevelSearch = normalizedSkillLevel.ToLowerInvariant();
            query = query.Where(listing =>
                listing.Player != null
                && listing.Player.PlayerSports.Any(playerSport =>
                    playerSport.IsActive
                    && playerSport.SkillLevel != null
                    && playerSport.SkillLevel.ToLower().Contains(skillLevelSearch)));
        }

        var normalizedZipCode = zipRadiusSearchService.NormalizeZipCode(zipCode);
        if (!string.IsNullOrWhiteSpace(normalizedZipCode))
        {
            query = query.Where(listing => listing.ZipCode == normalizedZipCode);
        }

        if (zipRadiusResult is not null)
        {
            var radiusZipCodes = zipRadiusResult.ZipCodes;
            query = query.Where(listing =>
                listing.ZipCode != null
                && radiusZipCodes.Contains(listing.ZipCode));
        }

        var normalizedCity = NormalizeOptional(city);
        if (!string.IsNullOrWhiteSpace(normalizedCity))
        {
            var citySearch = normalizedCity.ToLowerInvariant();
            query = query.Where(listing => listing.City != null && listing.City.ToLower().Contains(citySearch));
        }

        var normalizedState = NormalizeState(state);
        if (!string.IsNullOrWhiteSpace(normalizedState))
        {
            query = query.Where(listing => listing.State == normalizedState);
        }

        if (minPrice.HasValue)
        {
            query = query.Where(listing => listing.AskingPrice == null || listing.AskingPrice >= minPrice.Value);
        }

        if (maxPrice.HasValue)
        {
            query = query.Where(listing => listing.AskingPrice == null || listing.AskingPrice <= maxPrice.Value);
        }

        var normalizedSearch = NormalizeOptional(q);
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var search = normalizedSearch.ToLowerInvariant();
            query = query.Where(listing =>
                listing.Title.ToLower().Contains(search)
                || (listing.Description != null && listing.Description.ToLower().Contains(search))
                || (listing.City != null && listing.City.ToLower().Contains(search))
                || (listing.State != null && listing.State.ToLower().Contains(search))
                || (listing.ZipCode != null && listing.ZipCode.Contains(search)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var entitlingStatusActive = "active";
        var entitlingStatusTrialing = "trialing";

        IOrderedQueryable<PlayerListing> orderedQuery;
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var search = normalizedSearch.ToLowerInvariant();
            orderedQuery = query
                .OrderByDescending(listing =>
                    (listing.Title.ToLower().Contains(search) ? 5 : 0)
                    + (listing.Description != null && listing.Description.ToLower().Contains(search) ? 3 : 0)
                    + (listing.City != null && listing.City.ToLower().Contains(search) ? 2 : 0)
                    + (listing.State != null && listing.State.ToLower().Contains(search) ? 1 : 0)
                    + (listing.ZipCode != null && listing.ZipCode.Contains(search) ? 1 : 0))
                .ThenByDescending(listing => dbContext.Subscriptions.Any(subscription =>
                    subscription.UserId == listing.UserId
                    && (subscription.Status == entitlingStatusActive || subscription.Status == entitlingStatusTrialing)
                    && (subscription.PlanType == TryOutSpotPlanCodes.PremiumPlayer || subscription.IsElite)))
                .ThenByDescending(listing => listing.PublishedAt)
                .ThenByDescending(listing => listing.UpdatedAt);
        }
        else
        {
            orderedQuery = query
                .OrderByDescending(listing => dbContext.Subscriptions.Any(subscription =>
                    subscription.UserId == listing.UserId
                    && (subscription.Status == entitlingStatusActive || subscription.Status == entitlingStatusTrialing)
                    && (subscription.PlanType == TryOutSpotPlanCodes.PremiumPlayer || subscription.IsElite)))
                .ThenByDescending(listing => listing.PublishedAt)
                .ThenByDescending(listing => listing.UpdatedAt);
        }

        var listingEntities = await orderedQuery
            .Include(listing => listing.Player)
            .Include(listing => listing.Sport)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        var favoriteListingIds = await GetFavoritePlayerListingIdsAsync(listingEntities, cancellationToken);
        var listings = listingEntities
            .Select(listing => ToSummaryResponse(listing, favoriteListingIds))
            .ToArray();
        if (zipRadiusResult is not null)
        {
            var distanceByZipCode = zipRadiusResult.DistanceByZipCode;
            listings = listings
                .Select(listing => listing.ZipCode is null || !distanceByZipCode.TryGetValue(listing.ZipCode, out var distanceMiles)
                    ? listing
                    : listing with { DistanceMiles = Math.Round(distanceMiles, 1) })
                .ToArray();
        }

        return Ok(new PlayerListingListResponse(
            listings,
            page,
            pageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)pageSize),
            zipRadiusResult?.OriginZipCode,
            zipRadiusResult?.RadiusMiles,
            hasAdvancedPlayerSearch));
    }

    private async Task<bool> HasAdvancedPlayerSearchAsync(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return false;
        }

        return await entitlementService.HasFeatureAsync(
            userId,
            TryOutSpotFeatureCodes.AdvancedPlayerSearch,
            cancellationToken);
    }

    private bool IsSignedInTeamRepresentative()
    {
        return User.Claims
            .Where(claim => claim.Type == ClaimTypes.Role)
            .Select(claim => TryOutSpotRoles.NormalizePublicRegistrationRole(claim.Value))
            .Any(normalizedRole => string.Equals(normalizedRole, TryOutSpotRoles.TeamRepresentative, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Lists all active listings created by the signed-in account, including unpublished drafts.
    /// </summary>
    [HttpGet("mine")]
    [ProducesResponseType<PlayerListingListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PlayerListingListResponse>> Mine(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.PlayerListings
            .AsNoTracking()
            .Where(listing => listing.UserId == userId)
            .Where(listing => listing.IsActive);

        var totalCount = await query.CountAsync(cancellationToken);
        var listingEntities = await query
            .Include(listing => listing.Player)
            .Include(listing => listing.Sport)
            .OrderByDescending(listing => listing.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        var listings = listingEntities
            .Select(listing => ToSummaryResponse(listing))
            .ToArray();

        return Ok(new PlayerListingListResponse(
            listings,
            page,
            pageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    /// <summary>
    /// Returns a single owner listing.
    /// </summary>
    [HttpGet("mine/{listingId:guid}")]
    [ProducesResponseType<PlayerListingDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlayerListingDetailResponse>> MineById(Guid listingId, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var listing = await dbContext.PlayerListings
            .AsNoTracking()
            .Where(currentListing => currentListing.Id == listingId)
            .Where(currentListing => currentListing.UserId == userId)
            .Where(currentListing => currentListing.IsActive)
            .Include(currentListing => currentListing.Player)
            .Include(currentListing => currentListing.Sport)
            .SingleOrDefaultAsync(cancellationToken);

        return listing is null ? NotFound() : Ok(ToDetailResponse(listing));
    }

    /// <summary>
    /// Creates a new parent or player listing.
    /// </summary>
    [Authorize(Policy = CreateListingsPolicy)]
    [HttpPost]
    [ProducesResponseType<PlayerListingActionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PlayerListingActionResponse>> Create(
        CreatePlayerListingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var normalizedListingType = NormalizeListingType(request.ListingType, nameof(request.ListingType));
        if (request.ExpiresAt.HasValue && request.ExpiresAt.Value <= DateTime.UtcNow)
        {
            ModelState.AddModelError(nameof(request.ExpiresAt), "Expiration must be in the future.");
        }

        if (request.PlayerId.HasValue)
        {
            var canManagePlayer = await dbContext.UserPlayerRelationships
                .AsNoTracking()
                .AnyAsync(
                    relationship => relationship.UserId == userId
                        && relationship.PlayerId == request.PlayerId.Value
                        && relationship.CanManage,
                    cancellationToken);

            if (!canManagePlayer)
            {
                ModelState.AddModelError(nameof(request.PlayerId), "You can only list players you can manage.");
            }
        }

        if (request.SportId.HasValue)
        {
            var sportExists = await dbContext.Sports
                .AsNoTracking()
                .AnyAsync(sport => sport.Id == request.SportId.Value && sport.IsActive, cancellationToken);
            if (!sportExists)
            {
                ModelState.AddModelError(nameof(request.SportId), "Selected sport is not available.");
            }
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var now = DateTime.UtcNow;
        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(currentUser => currentUser.Id == userId, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var player = request.PlayerId.HasValue
            ? await dbContext.Players
                .AsNoTracking()
                .SingleOrDefaultAsync(currentPlayer => currentPlayer.Id == request.PlayerId.Value, cancellationToken)
            : null;

        var normalizedZipCode = NormalizeOptional(request.ZipCode) ?? player?.ZipCode ?? user.ZipCode;
        if (string.IsNullOrWhiteSpace(normalizedZipCode))
        {
            ModelState.AddModelError(nameof(request.ZipCode), "ZIP code is required for searchable listings.");
            return ValidationProblem(ModelState);
        }

        var listing = new PlayerListing
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PlayerId = request.PlayerId,
            SportId = request.SportId,
            ListingType = normalizedListingType!,
            Title = request.Title.Trim(),
            Description = NormalizeOptional(request.Description),
            AskingPrice = request.AskingPrice,
            Currency = NormalizeCurrency(request.Currency),
            Condition = NormalizeOptional(request.Condition),
            City = NormalizeOptional(request.City) ?? player?.City ?? user.City,
            State = NormalizeState(request.State) ?? player?.State ?? user.State,
            ZipCode = normalizedZipCode,
            IsSearchable = request.IsSearchable,
            IsPublished = request.IsPublished,
            PublishedAt = request.IsPublished ? now : null,
            ExpiresAt = NormalizeUtc(request.ExpiresAt),
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };

        dbContext.PlayerListings.Add(listing);
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = await dbContext.PlayerListings
            .AsNoTracking()
            .Where(currentListing => currentListing.Id == listing.Id)
            .Include(currentListing => currentListing.Player)
            .Include(currentListing => currentListing.Sport)
            .SingleAsync(cancellationToken);

        return StatusCode(
            StatusCodes.Status201Created,
            new PlayerListingActionResponse("Listing created.", ToDetailResponse(response)));
    }

    /// <summary>
    /// Updates a listing owned by the signed-in account.
    /// </summary>
    [Authorize(Policy = CreateListingsPolicy)]
    [HttpPost("{listingId:guid}/update")]
    [ProducesResponseType<PlayerListingActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlayerListingActionResponse>> Update(
        Guid listingId,
        UpdatePlayerListingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var listing = await dbContext.PlayerListings
            .SingleOrDefaultAsync(currentListing => currentListing.Id == listingId && currentListing.IsActive, cancellationToken);
        if (listing is null || listing.UserId != userId)
        {
            return NotFound();
        }

        var normalizedListingType = NormalizeListingType(request.ListingType, nameof(request.ListingType));
        if (request.ExpiresAt.HasValue && request.ExpiresAt.Value <= DateTime.UtcNow)
        {
            ModelState.AddModelError(nameof(request.ExpiresAt), "Expiration must be in the future.");
        }

        if (request.PlayerId.HasValue)
        {
            var canManagePlayer = await dbContext.UserPlayerRelationships
                .AsNoTracking()
                .AnyAsync(
                    relationship => relationship.UserId == userId
                        && relationship.PlayerId == request.PlayerId.Value
                        && relationship.CanManage,
                    cancellationToken);

            if (!canManagePlayer)
            {
                ModelState.AddModelError(nameof(request.PlayerId), "You can only list players you can manage.");
            }
        }

        if (request.SportId.HasValue)
        {
            var sportExists = await dbContext.Sports
                .AsNoTracking()
                .AnyAsync(sport => sport.Id == request.SportId.Value && sport.IsActive, cancellationToken);
            if (!sportExists)
            {
                ModelState.AddModelError(nameof(request.SportId), "Selected sport is not available.");
            }
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var owner = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(currentUser => currentUser.Id == userId, cancellationToken);
        if (owner is null)
        {
            return Unauthorized();
        }

        var player = request.PlayerId.HasValue
            ? await dbContext.Players
                .AsNoTracking()
                .SingleOrDefaultAsync(currentPlayer => currentPlayer.Id == request.PlayerId.Value, cancellationToken)
            : null;

        var normalizedZipCode = NormalizeOptional(request.ZipCode) ?? player?.ZipCode ?? owner.ZipCode;
        if (string.IsNullOrWhiteSpace(normalizedZipCode))
        {
            ModelState.AddModelError(nameof(request.ZipCode), "ZIP code is required for searchable listings.");
            return ValidationProblem(ModelState);
        }

        listing.PlayerId = request.PlayerId;
        listing.SportId = request.SportId;
        listing.ListingType = normalizedListingType!;
        listing.Title = request.Title.Trim();
        listing.Description = NormalizeOptional(request.Description);
        listing.AskingPrice = request.AskingPrice;
        listing.Currency = NormalizeCurrency(request.Currency);
        listing.Condition = NormalizeOptional(request.Condition);
        listing.City = NormalizeOptional(request.City) ?? player?.City ?? owner.City;
        listing.State = NormalizeState(request.State) ?? player?.State ?? owner.State;
        listing.ZipCode = normalizedZipCode;
        listing.IsSearchable = request.IsSearchable;
        listing.ExpiresAt = NormalizeUtc(request.ExpiresAt);
        listing.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        var response = await dbContext.PlayerListings
            .AsNoTracking()
            .Where(currentListing => currentListing.Id == listing.Id)
            .Include(currentListing => currentListing.Player)
            .Include(currentListing => currentListing.Sport)
            .SingleAsync(cancellationToken);

        return Ok(new PlayerListingActionResponse("Listing updated.", ToDetailResponse(response)));
    }

    /// <summary>
    /// Publishes or unpublishes a listing owned by the signed-in account.
    /// </summary>
    [Authorize(Policy = CreateListingsPolicy)]
    [HttpPost("{listingId:guid}/publication")]
    [ProducesResponseType<PlayerListingActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlayerListingActionResponse>> SetPublication(
        Guid listingId,
        SetPlayerListingPublicationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var listing = await dbContext.PlayerListings
            .SingleOrDefaultAsync(currentListing => currentListing.Id == listingId && currentListing.IsActive, cancellationToken);
        if (listing is null || listing.UserId != userId)
        {
            return NotFound();
        }

        var now = DateTime.UtcNow;
        listing.IsPublished = request.IsPublished;
        listing.PublishedAt = request.IsPublished
            ? listing.PublishedAt ?? now
            : null;
        listing.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        var response = await dbContext.PlayerListings
            .AsNoTracking()
            .Where(currentListing => currentListing.Id == listing.Id)
            .Include(currentListing => currentListing.Player)
            .Include(currentListing => currentListing.Sport)
            .SingleAsync(cancellationToken);

        return Ok(new PlayerListingActionResponse(
            request.IsPublished ? "Listing published." : "Listing unpublished.",
            ToDetailResponse(response)));
    }

    /// <summary>
    /// Deactivates a listing owned by the signed-in account.
    /// </summary>
    [HttpPost("{listingId:guid}/deactivate")]
    [ProducesResponseType<PlayerListingActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlayerListingActionResponse>> Deactivate(
        Guid listingId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var listing = await dbContext.PlayerListings
            .SingleOrDefaultAsync(currentListing => currentListing.Id == listingId && currentListing.IsActive, cancellationToken);
        if (listing is null || listing.UserId != userId)
        {
            return NotFound();
        }

        listing.IsActive = false;
        listing.IsPublished = false;
        listing.PublishedAt = null;
        listing.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = await dbContext.PlayerListings
            .AsNoTracking()
            .Where(currentListing => currentListing.Id == listing.Id)
            .Include(currentListing => currentListing.Player)
            .Include(currentListing => currentListing.Sport)
            .SingleAsync(cancellationToken);

        return Ok(new PlayerListingActionResponse("Listing deactivated.", ToDetailResponse(response)));
    }

    private string? NormalizeListingType(string? listingType, string modelStateKey)
    {
        var normalized = TryOutSpotPlayerListingTypes.Normalize(listingType);
        if (normalized is null)
        {
            ModelState.AddModelError(
                modelStateKey,
                $"'{listingType}' is not a supported listing type. Supported values: {string.Join(", ", TryOutSpotPlayerListingTypes.Values)}.");
        }

        return normalized;
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdClaim, out userId);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeState(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    }

    private static string? NormalizeCurrency(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return null;
        }

        var normalized = currency.Trim().ToUpperInvariant();
        return normalized.Length > 3 ? normalized[..3] : normalized;
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

    private async Task<IReadOnlySet<Guid>> GetFavoritePlayerListingIdsAsync(
        IReadOnlyCollection<PlayerListing> listings,
        CancellationToken cancellationToken)
    {
        if (listings.Count == 0 || !TryGetCurrentUserId(out var userId))
        {
            return new HashSet<Guid>();
        }

        var listingIds = listings
            .Select(listing => listing.Id)
            .ToArray();

        return (await dbContext.UserFavorites
                .AsNoTracking()
                .Where(favorite => favorite.UserId == userId)
                .Where(favorite => favorite.PlayerListingId != null)
                .Where(favorite => listingIds.Contains(favorite.PlayerListingId!.Value))
                .Select(favorite => favorite.PlayerListingId!.Value)
                .ToArrayAsync(cancellationToken))
            .ToHashSet();
    }

    private static PlayerListingSummaryResponse ToSummaryResponse(
        PlayerListing listing,
        IReadOnlySet<Guid>? favoriteListingIds = null)
    {
        return new PlayerListingSummaryResponse(
            listing.Id,
            listing.ListingType,
            listing.Title,
            listing.Description,
            listing.PlayerId,
            listing.Player == null ? null : $"{listing.Player.FirstName} {listing.Player.LastName}".Trim(),
            listing.SportId,
            listing.Sport == null ? null : listing.Sport.Name,
            listing.AskingPrice,
            listing.Currency,
            listing.Condition,
            listing.City,
            listing.State,
            listing.ZipCode,
            listing.IsPublished,
            listing.IsSearchable,
            listing.PublishedAt,
            listing.ExpiresAt,
            listing.UpdatedAt,
            IsFavorited: favoriteListingIds?.Contains(listing.Id) == true);
    }

    private static PlayerListingDetailResponse ToDetailResponse(PlayerListing listing)
    {
        return new PlayerListingDetailResponse(
            listing.Id,
            listing.UserId,
            listing.ListingType,
            listing.Title,
            listing.Description,
            listing.PlayerId,
            listing.Player == null ? null : $"{listing.Player.FirstName} {listing.Player.LastName}".Trim(),
            listing.SportId,
            listing.Sport == null ? null : listing.Sport.Name,
            listing.AskingPrice,
            listing.Currency,
            listing.Condition,
            listing.City,
            listing.State,
            listing.ZipCode,
            listing.IsPublished,
            listing.IsSearchable,
            listing.PublishedAt,
            listing.ExpiresAt,
            listing.CreatedAt,
            listing.UpdatedAt);
    }
}
