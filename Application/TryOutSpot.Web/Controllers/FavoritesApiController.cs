using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Models.Favorites;
using TryOutSpot.Web.Security;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Controllers;

/// <summary>
/// Favorite and bookmark endpoints for saved listings.
/// </summary>
[ApiController]
[Tags("Favorites")]
[Produces("application/json")]
[Route("api/favorites")]
[Authorize(Policy = TryOutSpotAuthorizationPolicies.ActiveUser)]
public sealed class FavoritesApiController(
    AppDbContext dbContext,
    IEntitlementService entitlementService) : ControllerBase
{
    /// <summary>
    /// Lists favorites saved by the signed-in account.
    /// </summary>
    [HttpGet("mine")]
    [ProducesResponseType<FavoriteListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<FavoriteListResponse>> Mine(
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

        var playerListingFavorites = await dbContext.UserFavorites
            .AsNoTracking()
            .Where(favorite => favorite.UserId == userId)
            .Where(favorite => favorite.PlayerListingId != null)
            .Include(favorite => favorite.PlayerListing)
                .ThenInclude(listing => listing!.Player)
            .Include(favorite => favorite.PlayerListing)
                .ThenInclude(listing => listing!.Sport)
            .OrderByDescending(favorite => favorite.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        var opportunityFavorites = await dbContext.UserFavorites
            .AsNoTracking()
            .Where(favorite => favorite.UserId == userId)
            .Where(favorite => favorite.OpportunityId != null)
            .Include(favorite => favorite.Opportunity)
                .ThenInclude(opportunity => opportunity!.Team)
                    .ThenInclude(team => team.Organization)
            .Include(favorite => favorite.Opportunity)
                .ThenInclude(opportunity => opportunity!.Sport)
            .OrderByDescending(favorite => favorite.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);

        return Ok(new FavoriteListResponse(
            playerListingFavorites
                .Where(favorite => favorite.PlayerListing is not null)
                .Select(ToPlayerListingFavoriteSummary)
                .ToArray(),
            opportunityFavorites
                .Where(favorite => favorite.Opportunity is not null)
                .Select(ToOpportunityFavoriteSummary)
                .ToArray()));
    }

    /// <summary>
    /// Saves a published player listing to favorites.
    /// </summary>
    [HttpPost("player-listings/{listingId:guid}")]
    [ProducesResponseType<FavoriteActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FavoriteActionResponse>> AddPlayerListing(
        Guid listingId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var listingExists = await QueryVisiblePlayerListing(listingId)
            .AnyAsync(cancellationToken);
        if (!listingExists)
        {
            return NotFound();
        }

        var exists = await dbContext.UserFavorites
            .AnyAsync(
                favorite => favorite.UserId == userId
                    && favorite.PlayerListingId == listingId,
                cancellationToken);
        if (!exists)
        {
            dbContext.UserFavorites.Add(new UserFavorite
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PlayerListingId = listingId,
                CreatedAt = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(new FavoriteActionResponse("Player listing saved to favorites.", IsFavorited: true));
    }

    /// <summary>
    /// Removes a player listing from favorites.
    /// </summary>
    [HttpPost("player-listings/{listingId:guid}/remove")]
    [ProducesResponseType<FavoriteActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<FavoriteActionResponse>> RemovePlayerListing(
        Guid listingId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var favorite = await dbContext.UserFavorites
            .SingleOrDefaultAsync(
                currentFavorite => currentFavorite.UserId == userId
                    && currentFavorite.PlayerListingId == listingId,
                cancellationToken);
        if (favorite is not null)
        {
            dbContext.UserFavorites.Remove(favorite);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(new FavoriteActionResponse("Player listing removed from favorites.", IsFavorited: false));
    }

    /// <summary>
    /// Saves a published opportunity listing to favorites.
    /// </summary>
    [HttpPost("opportunities/{opportunityId:guid}")]
    [ProducesResponseType<FavoriteActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FavoriteActionResponse>> AddOpportunity(
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var hasAdvancedOpportunitySearch = await entitlementService.HasFeatureAsync(
            userId,
            TryOutSpotFeatureCodes.AdvancedOpportunitySearch,
            cancellationToken);
        var opportunityExists = await QueryVisibleOpportunity(opportunityId, hasAdvancedOpportunitySearch)
            .AnyAsync(cancellationToken);
        if (!opportunityExists)
        {
            return NotFound();
        }

        var exists = await dbContext.UserFavorites
            .AnyAsync(
                favorite => favorite.UserId == userId
                    && favorite.OpportunityId == opportunityId,
                cancellationToken);
        if (!exists)
        {
            dbContext.UserFavorites.Add(new UserFavorite
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                OpportunityId = opportunityId,
                CreatedAt = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(new FavoriteActionResponse("Opportunity saved to favorites.", IsFavorited: true));
    }

    /// <summary>
    /// Removes an opportunity listing from favorites.
    /// </summary>
    [HttpPost("opportunities/{opportunityId:guid}/remove")]
    [ProducesResponseType<FavoriteActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<FavoriteActionResponse>> RemoveOpportunity(
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var userId))
        {
            return Unauthorized();
        }

        var favorite = await dbContext.UserFavorites
            .SingleOrDefaultAsync(
                currentFavorite => currentFavorite.UserId == userId
                    && currentFavorite.OpportunityId == opportunityId,
                cancellationToken);
        if (favorite is not null)
        {
            dbContext.UserFavorites.Remove(favorite);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Ok(new FavoriteActionResponse("Opportunity removed from favorites.", IsFavorited: false));
    }

    private IQueryable<PlayerListing> QueryVisiblePlayerListing(Guid listingId)
    {
        var now = DateTime.UtcNow;
        return dbContext.PlayerListings
            .AsNoTracking()
            .Where(listing => listing.Id == listingId)
            .Where(listing => listing.IsActive)
            .Where(listing => listing.IsPublished)
            .Where(listing => listing.IsSearchable)
            .Where(listing => listing.ExpiresAt == null || listing.ExpiresAt > now);
    }

    private IQueryable<Opportunity> QueryVisibleOpportunity(Guid opportunityId, bool hasAdvancedOpportunitySearch)
    {
        var now = DateTime.UtcNow;
        var query = dbContext.Opportunities
            .AsNoTracking()
            .Where(opportunity => opportunity.Id == opportunityId)
            .Where(opportunity => opportunity.IsActive)
            .Where(opportunity => opportunity.IsPublished)
            .Where(opportunity =>
                (opportunity.ListingEndDate ?? opportunity.ExpiresAt) == null
                || (opportunity.ListingEndDate ?? opportunity.ExpiresAt) > now)
            .Where(opportunity => opportunity.Team.IsActive)
            .Where(opportunity => opportunity.Team.IsSearchable);

        return hasAdvancedOpportunitySearch
            ? query
            : query.Where(opportunity => opportunity.ListingStartDate == null || opportunity.ListingStartDate <= now);
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdClaim, out userId);
    }

    private static PlayerListingFavoriteSummaryResponse ToPlayerListingFavoriteSummary(UserFavorite favorite)
    {
        var listing = favorite.PlayerListing!;
        return new PlayerListingFavoriteSummaryResponse(
            favorite.Id,
            listing.Id,
            listing.ListingType,
            listing.Title,
            listing.Player is null ? null : $"{listing.Player.FirstName} {listing.Player.LastName}".Trim(),
            listing.Sport?.Name,
            listing.City,
            listing.State,
            listing.ZipCode,
            favorite.CreatedAt);
    }

    private static OpportunityFavoriteSummaryResponse ToOpportunityFavoriteSummary(UserFavorite favorite)
    {
        var opportunity = favorite.Opportunity!;
        return new OpportunityFavoriteSummaryResponse(
            favorite.Id,
            opportunity.Id,
            opportunity.TeamId,
            opportunity.Team.Name,
            opportunity.Team.Organization?.Name,
            opportunity.Sport.Name,
            opportunity.Type,
            opportunity.Title,
            opportunity.EventDate,
            opportunity.City ?? opportunity.Team.City,
            opportunity.State ?? opportunity.Team.State,
            opportunity.ZipCode ?? opportunity.Team.ZipCode,
            favorite.CreatedAt);
    }
}
