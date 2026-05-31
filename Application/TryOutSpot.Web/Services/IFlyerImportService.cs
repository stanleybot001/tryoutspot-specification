using TryOutSpot.Web.Data.Entities;

namespace TryOutSpot.Web.Services;

public interface IFlyerImportService
{
    Task<FlyerImportMutationResult> CreateAsync(
        FlyerImportCreateInput input,
        UploadedFlyerImportFile? uploadedFile,
        Guid createdByUserId,
        CancellationToken cancellationToken);

    Task<FlyerImportMutationResult> UpdateAsync(
        Guid flyerImportId,
        FlyerImportCreateInput input,
        UploadedFlyerImportFile? uploadedFile,
        Guid reviewedByUserId,
        CancellationToken cancellationToken);

    Task<FlyerImportCreateListingResult> CreateListingAsync(
        Guid flyerImportId,
        Guid reviewedByUserId,
        bool publishImmediately,
        CancellationToken cancellationToken);

    Task<FlyerImportMutationResult> RejectAsync(
        Guid flyerImportId,
        Guid reviewedByUserId,
        string? adminNotes,
        CancellationToken cancellationToken);

    Task<StoredObjectPayload?> DownloadFlyerAsync(
        Guid flyerImportId,
        CancellationToken cancellationToken);
}

public sealed record FlyerImportCreateInput(
    string? SourcePlatform,
    string? SourceUrl,
    string? OriginalExternalImageUrl,
    Guid? SportId,
    string? SportName,
    string? OpportunityType,
    string? Title,
    string? TeamName,
    string? OrganizationName,
    string? AgeGroup,
    string? CompetitionLevel,
    DateTime? EventDate,
    DateTime? EventEndDate,
    DateTime? RegistrationDeadline,
    decimal? RegistrationFee,
    string? Location,
    string? Address,
    string? City,
    string? State,
    string? ZipCode,
    string? ContactEmail,
    string? ContactPhone,
    string? WebsiteUrl,
    string? Description,
    string? RequiredEquipment,
    string? WhatToBring,
    string? SpecialInstructions,
    string? ExtractedJson,
    string? ConfidenceJson,
    string? AdminNotes);

public sealed record UploadedFlyerImportFile(
    string FileName,
    byte[] Content,
    string ContentType);

public sealed record FlyerImportMutationResult(
    bool Succeeded,
    FlyerImport? FlyerImport,
    IReadOnlyCollection<string> Errors)
{
    public static FlyerImportMutationResult Success(FlyerImport flyerImport)
    {
        return new FlyerImportMutationResult(true, flyerImport, []);
    }

    public static FlyerImportMutationResult Failure(params string[] errors)
    {
        return new FlyerImportMutationResult(false, null, errors);
    }
}

public sealed record FlyerImportCreateListingResult(
    bool Succeeded,
    FlyerImport? FlyerImport,
    Guid? TeamId,
    Guid? OpportunityId,
    IReadOnlyCollection<string> Errors)
{
    public static FlyerImportCreateListingResult Success(FlyerImport flyerImport, Guid teamId, Guid opportunityId)
    {
        return new FlyerImportCreateListingResult(true, flyerImport, teamId, opportunityId, []);
    }

    public static FlyerImportCreateListingResult Failure(params string[] errors)
    {
        return new FlyerImportCreateListingResult(false, null, null, null, errors);
    }
}
