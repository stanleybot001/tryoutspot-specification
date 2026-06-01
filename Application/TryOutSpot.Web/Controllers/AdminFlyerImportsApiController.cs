using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Listings;
using TryOutSpot.Web.Models.Admin;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Controllers;

[ApiController]
[Tags("Admin Flyer Imports")]
[Produces("application/json")]
[Route("api/admin/flyer-imports")]
[Authorize(Roles = TryOutSpotRoles.PlatformAdmin)]
public sealed class AdminFlyerImportsApiController(
    AppDbContext dbContext,
    IFlyerImportService flyerImportService,
    IFlyerDuplicateDetectionService flyerDuplicateDetectionService) : ControllerBase
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    [HttpGet("")]
    [ProducesResponseType<FlyerImportListResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<FlyerImportListResponse>> List(
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

        return Ok(new FlyerImportListResponse(
            imports.Select(ToResponse).ToArray(),
            page,
            pageSize,
            totalCount,
            CalculateTotalPages(totalCount, pageSize)));
    }

    [HttpGet("{flyerImportId:guid}")]
    [ProducesResponseType<FlyerImportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FlyerImportResponse>> GetById(
        Guid flyerImportId,
        CancellationToken cancellationToken = default)
    {
        var flyerImport = await dbContext.FlyerImports
            .AsNoTracking()
            .SingleOrDefaultAsync(currentImport => currentImport.Id == flyerImportId, cancellationToken);
        return flyerImport is null ? NotFound() : Ok(ToResponse(flyerImport));
    }

    [HttpPost("")]
    [ProducesResponseType<FlyerImportActionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<FlyerImportActionResponse>> Create(
        CreateFlyerImportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var adminUserId))
        {
            return Unauthorized();
        }

        var result = await flyerImportService.CreateAsync(
            ToInput(request),
            uploadedFile: null,
            adminUserId,
            cancellationToken);
        if (!result.Succeeded || result.FlyerImport is null)
        {
            return ValidationProblemFromErrors(result.Errors);
        }

        var response = new FlyerImportActionResponse("Flyer import queued.", ToResponse(result.FlyerImport));
        return CreatedAtAction(nameof(GetById), new { flyerImportId = result.FlyerImport.Id }, response);
    }

    [HttpPost("upload")]
    [Consumes("multipart/form-data")]
    [ProducesResponseType<FlyerImportActionResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<FlyerImportActionResponse>> Upload(
        [FromForm] CreateFlyerImportUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var adminUserId))
        {
            return Unauthorized();
        }

        var upload = await FlyerImportUploadHelper.ParseAsync(request.FlyerFile, cancellationToken);
        if (!upload.Succeeded)
        {
            return ValidationProblemFromErrors(upload.Errors);
        }

        var result = await flyerImportService.CreateAsync(
            ToInput(request),
            upload.File,
            adminUserId,
            cancellationToken);
        if (!result.Succeeded || result.FlyerImport is null)
        {
            return ValidationProblemFromErrors(result.Errors);
        }

        var response = new FlyerImportActionResponse("Flyer import queued.", ToResponse(result.FlyerImport));
        return CreatedAtAction(nameof(GetById), new { flyerImportId = result.FlyerImport.Id }, response);
    }

    [HttpPost("{flyerImportId:guid}/create-listing")]
    [ProducesResponseType<FlyerImportActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FlyerImportActionResponse>> CreateListing(
        Guid flyerImportId,
        CreateListingFromFlyerImportRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCurrentUserId(out var adminUserId))
        {
            return Unauthorized();
        }

        var duplicateCheck = await flyerDuplicateDetectionService.FindDuplicatesAsync(flyerImportId, cancellationToken);
        if (duplicateCheck.HasBlockingDuplicate && !request.ConfirmDuplicateOverride)
        {
            var topCandidate = duplicateCheck.TopCandidate;
            return ValidationProblemFromErrors(
            [
                topCandidate is null
                    ? "Possible duplicate found. Review the match before creating a listing."
                    : $"Possible duplicate found ({topCandidate.ProbabilityPercent}% match). Send ConfirmDuplicateOverride=true to create anyway."
            ]);
        }

        var result = await flyerImportService.CreateListingAsync(
            flyerImportId,
            adminUserId,
            request.PublishImmediately,
            cancellationToken);
        if (!result.Succeeded || result.FlyerImport is null)
        {
            return result.Errors.Contains("Flyer import was not found.", StringComparer.Ordinal)
                ? NotFound()
                : ValidationProblemFromErrors(result.Errors);
        }

        return Ok(new FlyerImportActionResponse(
            request.PublishImmediately ? "Listing published." : "Draft listing created.",
            ToResponse(result.FlyerImport),
            result.TeamId,
            result.OpportunityId));
    }

    private IQueryable<FlyerImport> BuildFlyerImportQuery(string? search, string? status)
    {
        var query = dbContext.FlyerImports.AsNoTracking();
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
                || (flyerImport.SourceUrl != null && flyerImport.SourceUrl.ToLower().Contains(loweredSearch))
                || (flyerImport.OriginalExternalImageUrl != null && flyerImport.OriginalExternalImageUrl.ToLower().Contains(loweredSearch)));
        }

        return query;
    }

    private ActionResult ValidationProblemFromErrors(IReadOnlyCollection<string> errors)
    {
        foreach (var error in errors)
        {
            ModelState.AddModelError(string.Empty, error);
        }

        return ValidationProblem(ModelState);
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdClaim, out userId);
    }

    private static FlyerImportCreateInput ToInput(CreateFlyerImportRequest request)
    {
        return new FlyerImportCreateInput(
            request.SourcePlatform,
            request.SourceUrl,
            request.OriginalExternalImageUrl,
            request.SportId,
            request.SportName,
            request.OpportunityType,
            request.Title,
            request.TeamName,
            request.OrganizationName,
            request.AgeGroup,
            request.CompetitionLevel,
            request.EventDate,
            request.EventEndDate,
            request.RegistrationDeadline,
            request.RegistrationFee,
            request.Location,
            request.Address,
            request.City,
            request.State,
            request.ZipCode,
            request.ContactEmail,
            request.ContactPhone,
            request.WebsiteUrl,
            request.Description,
            request.RequiredEquipment,
            request.WhatToBring,
            request.SpecialInstructions,
            request.ExtractedJson,
            request.ConfidenceJson,
            request.AdminNotes);
    }

    private static FlyerImportResponse ToResponse(FlyerImport flyerImport)
    {
        return new FlyerImportResponse(
            flyerImport.Id,
            flyerImport.Status,
            flyerImport.SourcePlatform,
            flyerImport.SourceUrl,
            flyerImport.OriginalExternalImageUrl,
            !string.IsNullOrWhiteSpace(flyerImport.StoredObjectKey),
            flyerImport.StoredFileName,
            flyerImport.StoredContentType,
            flyerImport.Title,
            flyerImport.TeamName,
            flyerImport.OrganizationName,
            flyerImport.SportId,
            flyerImport.SportName,
            flyerImport.OpportunityType,
            flyerImport.AgeGroup,
            flyerImport.CompetitionLevel,
            flyerImport.EventDate,
            flyerImport.EventEndDate,
            flyerImport.RegistrationDeadline,
            flyerImport.RegistrationFee,
            flyerImport.Location,
            flyerImport.Address,
            flyerImport.City,
            flyerImport.State,
            flyerImport.ZipCode,
            flyerImport.ContactEmail,
            flyerImport.ContactPhone,
            flyerImport.WebsiteUrl,
            flyerImport.Description,
            flyerImport.RequiredEquipment,
            flyerImport.WhatToBring,
            flyerImport.SpecialInstructions,
            flyerImport.ExtractedJson,
            flyerImport.ConfidenceJson,
            flyerImport.AdminNotes,
            flyerImport.TeamId,
            flyerImport.OpportunityId,
            flyerImport.CreatedAt,
            flyerImport.UpdatedAt);
    }

    private static int CalculateTotalPages(int totalCount, int pageSize)
    {
        return totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
