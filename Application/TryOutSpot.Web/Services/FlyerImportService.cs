using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Listings;

namespace TryOutSpot.Web.Services;

public sealed class FlyerImportService(
    AppDbContext dbContext,
    IPdfStorageService pdfStorageService,
    ILogger<FlyerImportService> logger) : IFlyerImportService
{
    private const string DefaultSourcePlatform = "manual";
    private const string FlyerImportDocumentType = "flyer-imports";
    private const string OpportunityDocumentType = "opportunities";

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

    public async Task<FlyerImportMutationResult> CreateAsync(
        FlyerImportCreateInput input,
        UploadedFlyerImportFile? uploadedFile,
        Guid createdByUserId,
        CancellationToken cancellationToken)
    {
        var errors = ValidateImportInput(input, uploadedFileRequired: uploadedFile is null);
        if (errors.Count > 0)
        {
            return new FlyerImportMutationResult(false, null, errors);
        }

        var now = DateTime.UtcNow;
        var flyerImport = new FlyerImport
        {
            Id = Guid.NewGuid(),
            SourcePlatform = NormalizeOptional(input.SourcePlatform) ?? DefaultSourcePlatform,
            Status = TryOutSpotFlyerImportStatuses.PendingReview,
            CreatedByUserId = createdByUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        ApplyInput(flyerImport, input);

        if (uploadedFile is not null)
        {
            var uploadResult = await UploadFileAsync(flyerImport, uploadedFile, createdByUserId, cancellationToken);
            if (!uploadResult.Succeeded)
            {
                return uploadResult;
            }
        }

        dbContext.FlyerImports.Add(flyerImport);
        await dbContext.SaveChangesAsync(cancellationToken);

        return FlyerImportMutationResult.Success(flyerImport);
    }

    public async Task<FlyerImportMutationResult> UpdateAsync(
        Guid flyerImportId,
        FlyerImportCreateInput input,
        UploadedFlyerImportFile? uploadedFile,
        Guid reviewedByUserId,
        CancellationToken cancellationToken)
    {
        var flyerImport = await dbContext.FlyerImports
            .SingleOrDefaultAsync(currentImport => currentImport.Id == flyerImportId, cancellationToken);
        if (flyerImport is null)
        {
            return FlyerImportMutationResult.Failure("Flyer import was not found.");
        }

        var errors = ValidateImportInput(
            input,
            uploadedFileRequired: uploadedFile is null
                && string.IsNullOrWhiteSpace(flyerImport.StoredObjectKey)
                && string.IsNullOrWhiteSpace(flyerImport.SourceUrl)
                && string.IsNullOrWhiteSpace(flyerImport.OriginalExternalImageUrl));
        if (errors.Count > 0)
        {
            return new FlyerImportMutationResult(false, null, errors);
        }

        var previousObjectKey = flyerImport.StoredObjectKey;
        ApplyInput(flyerImport, input);
        flyerImport.ReviewedByUserId = reviewedByUserId;
        flyerImport.ReviewedAt = DateTime.UtcNow;
        flyerImport.UpdatedAt = flyerImport.ReviewedAt.Value;

        if (uploadedFile is not null)
        {
            var uploadResult = await UploadFileAsync(flyerImport, uploadedFile, reviewedByUserId, cancellationToken);
            if (!uploadResult.Succeeded)
            {
                return uploadResult;
            }

            if (!string.IsNullOrWhiteSpace(previousObjectKey)
                && !string.Equals(previousObjectKey, flyerImport.StoredObjectKey, StringComparison.Ordinal))
            {
                await pdfStorageService.DeletePdfAsync(previousObjectKey, cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return FlyerImportMutationResult.Success(flyerImport);
    }

    public async Task<FlyerImportCreateListingResult> CreateListingAsync(
        Guid flyerImportId,
        Guid reviewedByUserId,
        bool publishImmediately,
        CancellationToken cancellationToken)
    {
        var flyerImport = await dbContext.FlyerImports
            .Include(currentImport => currentImport.Team)
            .Include(currentImport => currentImport.Opportunity)
            .SingleOrDefaultAsync(currentImport => currentImport.Id == flyerImportId, cancellationToken);
        if (flyerImport is null)
        {
            return FlyerImportCreateListingResult.Failure("Flyer import was not found.");
        }

        if (flyerImport.OpportunityId.HasValue)
        {
            return FlyerImportCreateListingResult.Failure("This flyer import already has a listing.");
        }

        if (string.Equals(flyerImport.Status, TryOutSpotFlyerImportStatuses.Rejected, StringComparison.Ordinal))
        {
            return FlyerImportCreateListingResult.Failure("Rejected flyer imports cannot create listings.");
        }

        var errors = new List<string>();
        var sport = await ResolveSportAsync(flyerImport, cancellationToken);
        if (sport is null)
        {
            errors.Add("Choose a sport before creating the listing.");
        }

        var teamName = NormalizeOptional(flyerImport.TeamName) ?? NormalizeOptional(flyerImport.OrganizationName);
        if (teamName is null)
        {
            errors.Add("Add the team name before creating the listing.");
        }

        if (publishImmediately && flyerImport.EventDate is null)
        {
            errors.Add("Add the event date before publishing this listing.");
        }

        if (publishImmediately && string.IsNullOrWhiteSpace(flyerImport.ZipCode))
        {
            errors.Add("Add a ZIP code before publishing this listing.");
        }

        if (errors.Count > 0 || sport is null || teamName is null)
        {
            return new FlyerImportCreateListingResult(false, flyerImport, null, null, errors);
        }

        var now = DateTime.UtcNow;
        var team = await ResolveOrCreateTeamAsync(flyerImport, sport, teamName, now, cancellationToken);
        await EnsureTeamSportAsync(team, sport, flyerImport, now, cancellationToken);

        var opportunityId = Guid.NewGuid();
        var opportunityFlyer = await TryBuildOpportunityFlyerReferenceAsync(
            flyerImport,
            opportunityId,
            reviewedByUserId,
            cancellationToken);
        if (!opportunityFlyer.Succeeded)
        {
            return new FlyerImportCreateListingResult(false, flyerImport, null, null, opportunityFlyer.Errors);
        }

        var normalizedOpportunityType = NormalizeOpportunityType(flyerImport.OpportunityType);
        var registrationRequired = string.Equals(normalizedOpportunityType, "tryout", StringComparison.Ordinal)
            && (flyerImport.RegistrationDeadline.HasValue
                || flyerImport.RegistrationFee.GetValueOrDefault() > 0);
        var opportunity = new Opportunity
        {
            Id = opportunityId,
            TeamId = team.Id,
            SportId = sport.Id,
            Type = normalizedOpportunityType,
            Title = ResolveOpportunityTitle(flyerImport, teamName),
            Description = NormalizeLength(flyerImport.Description, 4000),
            CompetitionLevel = NormalizeLength(flyerImport.CompetitionLevel, 100),
            AgeGroup = NormalizeLength(flyerImport.AgeGroup, 50),
            RegistrationRequired = registrationRequired,
            RegistrationDeadline = NormalizeUtc(flyerImport.RegistrationDeadline),
            RegistrationFee = Math.Max(0, flyerImport.RegistrationFee.GetValueOrDefault()),
            EventDate = NormalizeUtc(flyerImport.EventDate),
            EventEndDate = NormalizeUtc(flyerImport.EventEndDate),
            ListingStartDate = now,
            ListingEndDate = null,
            ExpiresAt = ResolveOpportunityExpiration(flyerImport),
            Location = NormalizeLength(flyerImport.Location, 500),
            Address = NormalizeLength(flyerImport.Address, 500),
            City = NormalizeLength(flyerImport.City, 100) ?? team.City,
            State = NormalizeState(flyerImport.State) ?? team.State,
            ZipCode = NormalizeLength(flyerImport.ZipCode, 10) ?? team.ZipCode,
            ContactEmail = NormalizeLength(flyerImport.ContactEmail, 255) ?? team.Email,
            ContactPhone = NormalizeLength(flyerImport.ContactPhone, 20) ?? team.PhoneNumber,
            WebsiteUrl = NormalizeLength(flyerImport.WebsiteUrl, 500) ?? team.WebsiteUrl,
            UploadedPdfObjectKey = opportunityFlyer.ObjectKey,
            UploadedPdfFileName = opportunityFlyer.FileName,
            RequiredEquipment = NormalizeLength(flyerImport.RequiredEquipment, 1000),
            WhatToBring = NormalizeLength(flyerImport.WhatToBring, 1000),
            SpecialInstructions = NormalizeLength(flyerImport.SpecialInstructions, 2000),
            IsPublished = publishImmediately,
            PublishedAt = publishImmediately ? now : null,
            ViewCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };

        dbContext.Opportunities.Add(opportunity);

        flyerImport.TeamId = team.Id;
        flyerImport.OpportunityId = opportunity.Id;
        flyerImport.Status = publishImmediately
            ? TryOutSpotFlyerImportStatuses.Published
            : TryOutSpotFlyerImportStatuses.DraftCreated;
        flyerImport.ReviewedByUserId = reviewedByUserId;
        flyerImport.ReviewedAt = now;
        flyerImport.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        return FlyerImportCreateListingResult.Success(flyerImport, team.Id, opportunity.Id);
    }

    public async Task<FlyerImportMutationResult> RejectAsync(
        Guid flyerImportId,
        Guid reviewedByUserId,
        string? adminNotes,
        CancellationToken cancellationToken)
    {
        var flyerImport = await dbContext.FlyerImports
            .SingleOrDefaultAsync(currentImport => currentImport.Id == flyerImportId, cancellationToken);
        if (flyerImport is null)
        {
            return FlyerImportMutationResult.Failure("Flyer import was not found.");
        }

        var now = DateTime.UtcNow;
        flyerImport.Status = TryOutSpotFlyerImportStatuses.Rejected;
        flyerImport.AdminNotes = NormalizeLength(adminNotes, 2000);
        flyerImport.ReviewedByUserId = reviewedByUserId;
        flyerImport.ReviewedAt = now;
        flyerImport.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        return FlyerImportMutationResult.Success(flyerImport);
    }

    public async Task<StoredObjectPayload?> DownloadFlyerAsync(
        Guid flyerImportId,
        CancellationToken cancellationToken)
    {
        var objectKey = await dbContext.FlyerImports
            .AsNoTracking()
            .Where(flyerImport => flyerImport.Id == flyerImportId)
            .Select(flyerImport => flyerImport.StoredObjectKey)
            .SingleOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(objectKey)
            ? null
            : await pdfStorageService.DownloadFileAsync(objectKey, cancellationToken);
    }

    private async Task<FlyerImportMutationResult> UploadFileAsync(
        FlyerImport flyerImport,
        UploadedFlyerImportFile uploadedFile,
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            var objectKey = BuildObjectKey(FlyerImportDocumentType, flyerImport.Id, userId, uploadedFile.FileName);
            await pdfStorageService.UploadFileAsync(
                objectKey,
                uploadedFile.Content,
                uploadedFile.ContentType,
                cancellationToken);

            flyerImport.StoredObjectKey = objectKey;
            flyerImport.StoredFileName = uploadedFile.FileName;
            flyerImport.StoredContentType = uploadedFile.ContentType;
            flyerImport.ContentHash = Convert.ToHexString(SHA256.HashData(uploadedFile.Content)).ToLowerInvariant();
            return FlyerImportMutationResult.Success(flyerImport);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to upload flyer import {FlyerImportId}.", flyerImport.Id);
            return FlyerImportMutationResult.Failure("We could not upload the flyer right now. Please try again.");
        }
    }

    private async Task<OpportunityFlyerReferenceResult> TryBuildOpportunityFlyerReferenceAsync(
        FlyerImport flyerImport,
        Guid opportunityId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(flyerImport.StoredObjectKey))
        {
            return OpportunityFlyerReferenceResult.Success(null, null);
        }

        try
        {
            var payload = await pdfStorageService.DownloadFileAsync(flyerImport.StoredObjectKey, cancellationToken);
            if (payload is null || payload.Content.Length == 0)
            {
                return OpportunityFlyerReferenceResult.Failure("The stored flyer could not be found. Upload it again before creating the listing.");
            }

            var fileName = NormalizeOptional(flyerImport.StoredFileName) ?? "listing-flyer";
            var objectKey = BuildObjectKey(OpportunityDocumentType, opportunityId, userId, fileName);
            await pdfStorageService.UploadFileAsync(
                objectKey,
                payload.Content,
                ResolveStoredContentType(payload.ContentType, flyerImport.StoredContentType),
                cancellationToken);

            return OpportunityFlyerReferenceResult.Success(objectKey, fileName);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to copy flyer import {FlyerImportId} to opportunity {OpportunityId}.", flyerImport.Id, opportunityId);
            return OpportunityFlyerReferenceResult.Failure("We could not attach the flyer to the listing right now. Please try again.");
        }
    }

    private async Task<Sport?> ResolveSportAsync(FlyerImport flyerImport, CancellationToken cancellationToken)
    {
        if (flyerImport.SportId.HasValue)
        {
            var sportById = await dbContext.Sports
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    sport => sport.Id == flyerImport.SportId.Value && sport.IsActive,
                    cancellationToken);
            if (sportById is not null)
            {
                return sportById;
            }
        }

        var sportName = NormalizeOptional(flyerImport.SportName);
        if (sportName is null)
        {
            return null;
        }

        var loweredSportName = sportName.ToLowerInvariant();
        return await dbContext.Sports
            .AsNoTracking()
            .OrderBy(sport => sport.Name)
            .FirstOrDefaultAsync(
                sport => sport.IsActive && sport.Name.ToLower() == loweredSportName,
                cancellationToken);
    }

    private async Task<Team> ResolveOrCreateTeamAsync(
        FlyerImport flyerImport,
        Sport sport,
        string teamName,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var loweredTeamName = teamName.ToLowerInvariant();
        var candidates = await dbContext.Teams
            .Include(team => team.TeamSports)
            .Where(team => team.IsActive && team.Name.ToLower() == loweredTeamName)
            .OrderByDescending(team => team.UpdatedAt)
            .ToArrayAsync(cancellationToken);

        var existingTeam = candidates.FirstOrDefault(team =>
            team.TeamSports.Any(teamSport => teamSport.IsActive && teamSport.SportId == sport.Id))
            ?? candidates.FirstOrDefault();
        if (existingTeam is not null)
        {
            return existingTeam;
        }

        var team = new Team
        {
            Id = Guid.NewGuid(),
            Name = NormalizeLength(teamName, 200) ?? teamName,
            TeamLevel = NormalizeLength(flyerImport.AgeGroup, 50),
            GeographicScope = "Local",
            Description = NormalizeLength(flyerImport.OrganizationName, 2000),
            WebsiteUrl = NormalizeLength(flyerImport.WebsiteUrl, 500),
            Address = NormalizeLength(flyerImport.Address, 500),
            City = NormalizeLength(flyerImport.City, 100),
            State = NormalizeState(flyerImport.State),
            ZipCode = NormalizeLength(flyerImport.ZipCode, 10),
            PhoneNumber = NormalizeLength(flyerImport.ContactPhone, 20),
            Email = NormalizeLength(flyerImport.ContactEmail, 255),
            IsSearchable = true,
            IsContactInfoVisible = true,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };

        dbContext.Teams.Add(team);
        return team;
    }

    private async Task EnsureTeamSportAsync(
        Team team,
        Sport sport,
        FlyerImport flyerImport,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var existingTeamSport = team.TeamSports.FirstOrDefault(teamSport =>
            teamSport.SportId == sport.Id && teamSport.IsActive);
        if (existingTeamSport is not null)
        {
            if (string.IsNullOrWhiteSpace(existingTeamSport.AgeGroup))
            {
                existingTeamSport.AgeGroup = NormalizeLength(flyerImport.AgeGroup, 50);
            }

            if (string.IsNullOrWhiteSpace(existingTeamSport.CompetitionLevel))
            {
                existingTeamSport.CompetitionLevel = NormalizeLength(flyerImport.CompetitionLevel, 100);
            }

            return;
        }

        var hasTrackedTeamSport = dbContext.ChangeTracker
            .Entries<TeamSport>()
            .Any(entry => entry.Entity.TeamId == team.Id && entry.Entity.SportId == sport.Id && entry.Entity.IsActive);
        if (hasTrackedTeamSport)
        {
            return;
        }

        var alreadyExists = await dbContext.TeamSports
            .AsNoTracking()
            .AnyAsync(teamSport =>
                teamSport.TeamId == team.Id
                && teamSport.SportId == sport.Id
                && teamSport.IsActive,
                cancellationToken);
        if (alreadyExists)
        {
            return;
        }

        dbContext.TeamSports.Add(new TeamSport
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            SportId = sport.Id,
            CompetitionLevel = NormalizeLength(flyerImport.CompetitionLevel, 100),
            AgeGroup = NormalizeLength(flyerImport.AgeGroup, 50),
            TravelLevel = team.GeographicScope,
            IsActive = true,
            CreatedAt = now
        });
    }

    private static void ApplyInput(FlyerImport flyerImport, FlyerImportCreateInput input)
    {
        flyerImport.SourcePlatform = NormalizeLength(input.SourcePlatform, 40) ?? flyerImport.SourcePlatform;
        flyerImport.SourceUrl = NormalizeLength(input.SourceUrl, 2000);
        flyerImport.OriginalExternalImageUrl = NormalizeLength(input.OriginalExternalImageUrl, 2000);
        flyerImport.SportId = input.SportId;
        flyerImport.SportName = NormalizeLength(input.SportName, 100);
        flyerImport.OpportunityType = NormalizeLength(input.OpportunityType, 50);
        flyerImport.Title = NormalizeLength(input.Title, 300);
        flyerImport.TeamName = NormalizeLength(input.TeamName, 200);
        flyerImport.OrganizationName = NormalizeLength(input.OrganizationName, 200);
        flyerImport.AgeGroup = NormalizeLength(input.AgeGroup, 50);
        flyerImport.CompetitionLevel = NormalizeLength(input.CompetitionLevel, 100);
        flyerImport.EventDate = NormalizeUtc(input.EventDate);
        flyerImport.EventEndDate = NormalizeUtc(input.EventEndDate);
        flyerImport.RegistrationDeadline = NormalizeUtc(input.RegistrationDeadline);
        flyerImport.RegistrationFee = input.RegistrationFee is < 0 ? null : input.RegistrationFee;
        flyerImport.Location = NormalizeLength(input.Location, 500);
        flyerImport.Address = NormalizeLength(input.Address, 500);
        flyerImport.City = NormalizeLength(input.City, 100);
        flyerImport.State = NormalizeState(input.State);
        flyerImport.ZipCode = NormalizeLength(input.ZipCode, 10);
        flyerImport.ContactEmail = NormalizeLength(input.ContactEmail, 255);
        flyerImport.ContactPhone = NormalizeLength(input.ContactPhone, 20);
        flyerImport.WebsiteUrl = NormalizeLength(input.WebsiteUrl, 500);
        flyerImport.Description = NormalizeLength(input.Description, 4000);
        flyerImport.RequiredEquipment = NormalizeLength(input.RequiredEquipment, 1000);
        flyerImport.WhatToBring = NormalizeLength(input.WhatToBring, 1000);
        flyerImport.SpecialInstructions = NormalizeLength(input.SpecialInstructions, 2000);
        flyerImport.ExtractedJson = NormalizeOptional(input.ExtractedJson);
        flyerImport.ConfidenceJson = NormalizeOptional(input.ConfidenceJson);
        flyerImport.AdminNotes = NormalizeLength(input.AdminNotes, 2000);
    }

    private static List<string> ValidateImportInput(
        FlyerImportCreateInput input,
        bool uploadedFileRequired)
    {
        var errors = new List<string>();
        if (uploadedFileRequired
            && string.IsNullOrWhiteSpace(input.SourceUrl)
            && string.IsNullOrWhiteSpace(input.OriginalExternalImageUrl))
        {
            errors.Add("Add a flyer file, source URL, or external image URL.");
        }

        if (input.RegistrationFee is < 0)
        {
            errors.Add("Registration fee cannot be negative.");
        }

        if (!IsValidJson(input.ExtractedJson))
        {
            errors.Add("Extracted JSON must be valid JSON.");
        }

        if (!IsValidJson(input.ConfidenceJson))
        {
            errors.Add("Confidence JSON must be valid JSON.");
        }

        return errors;
    }

    private static bool IsValidJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return true;
        }

        try
        {
            using var _ = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
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

    private static string ResolveOpportunityTitle(FlyerImport flyerImport, string teamName)
    {
        var title = NormalizeLength(flyerImport.Title, 300);
        if (title is not null)
        {
            return title;
        }

        var type = NormalizeOpportunityType(flyerImport.OpportunityType).Replace('_', ' ');
        return $"{teamName} {type}";
    }

    private static DateTime? ResolveOpportunityExpiration(FlyerImport flyerImport)
    {
        var eventEnd = NormalizeUtc(flyerImport.EventEndDate);
        if (eventEnd.HasValue)
        {
            return eventEnd.Value.AddDays(1);
        }

        var eventStart = NormalizeUtc(flyerImport.EventDate);
        return eventStart?.AddDays(1);
    }

    private static string BuildObjectKey(string documentType, Guid itemId, Guid userId, string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        return $"{documentType}/{itemId}/{DateTime.UtcNow:yyyyMMddHHmmss}-{userId}-{safeFileName}";
    }

    private static string ResolveStoredContentType(string? downloadedContentType, string? importContentType)
    {
        return NormalizeOptional(downloadedContentType)
            ?? NormalizeOptional(importContentType)
            ?? "application/octet-stream";
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
        var normalized = NormalizeOptional(state)?.ToUpperInvariant();
        return normalized is null
            ? null
            : NormalizeLength(normalized, 2);
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

    private sealed record OpportunityFlyerReferenceResult(
        bool Succeeded,
        string? ObjectKey,
        string? FileName,
        IReadOnlyCollection<string> Errors)
    {
        public static OpportunityFlyerReferenceResult Success(string? objectKey, string? fileName)
        {
            return new OpportunityFlyerReferenceResult(true, objectKey, fileName, []);
        }

        public static OpportunityFlyerReferenceResult Failure(params string[] errors)
        {
            return new OpportunityFlyerReferenceResult(false, null, null, errors);
        }
    }
}
