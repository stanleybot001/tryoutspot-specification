using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Listings;
using TryOutSpot.Web.Models.Listings;
using TryOutSpot.Web.Security;

namespace TryOutSpot.Web.Controllers;

/// <summary>
/// Parent and player listing endpoints for discovery-focused classifieds.
/// </summary>
[ApiController]
[Tags("Player Listings")]
[Produces("application/json")]
[Route("api/player-listings")]
[Authorize(Policy = TryOutSpotAuthorizationPolicies.ActiveUser)]
public sealed class PlayerListingsApiController(AppDbContext dbContext) : ControllerBase
{
    private const string CreateListingsPolicy =
        TryOutSpotAuthorizationPolicies.FeaturePolicyPrefix + TryOutSpotFeatureCodes.CreatePlayerListings;

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
        [FromQuery] string? zipCode,
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

        string? normalizedListingType = null;
        if (!string.IsNullOrWhiteSpace(listingType))
        {
            normalizedListingType = NormalizeListingType(listingType, nameof(listingType));
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

        var normalizedZipCode = NormalizeOptional(zipCode);
        if (!string.IsNullOrWhiteSpace(normalizedZipCode))
        {
            query = query.Where(listing => listing.ZipCode == normalizedZipCode);
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
        var listingEntities = await query
            .Include(listing => listing.Player)
            .Include(listing => listing.Sport)
            .OrderByDescending(listing => listing.PublishedAt)
            .ThenByDescending(listing => listing.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        var listings = listingEntities
            .Select(ToSummaryResponse)
            .ToArray();

        return Ok(new PlayerListingListResponse(
            listings,
            page,
            pageSize,
            totalCount,
            (int)Math.Ceiling(totalCount / (double)pageSize)));
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
            .Select(ToSummaryResponse)
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

    private static PlayerListingSummaryResponse ToSummaryResponse(PlayerListing listing)
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
            listing.UpdatedAt);
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
