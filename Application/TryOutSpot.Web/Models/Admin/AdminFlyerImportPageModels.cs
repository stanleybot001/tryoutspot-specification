using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Models.Admin;

public sealed class AdminFlyerImportListPageModel
{
    public IReadOnlyCollection<AdminFlyerImportListItem> Imports { get; set; } = [];

    public string? Search { get; set; }

    public string? Status { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public int TotalPages { get; set; }

    public IReadOnlyCollection<string> StatusOptions { get; set; } = [];
}

public sealed record AdminFlyerImportListItem(
    Guid ImportId,
    string Status,
    string SourcePlatform,
    string? Title,
    string? TeamName,
    string? SportName,
    DateTime? EventDateUtc,
    string? City,
    string? State,
    string? ZipCode,
    bool HasStoredFlyer,
    Guid? TeamId,
    Guid? OpportunityId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed class AdminFlyerImportDetailPageModel
{
    public AdminFlyerImportDetailItem Import { get; set; } = null!;

    public AdminFlyerImportForm Form { get; set; } = new();

    public IReadOnlyCollection<AdminSportOption> SportOptions { get; set; } = [];

    public FlyerDuplicateCheckResult? DuplicateCheck { get; set; }
}

public sealed record AdminFlyerImportDetailItem(
    Guid ImportId,
    string Status,
    string SourcePlatform,
    string? SourceUrl,
    string? OriginalExternalImageUrl,
    bool HasStoredFlyer,
    string? StoredFileName,
    string? StoredContentType,
    string? ContentHash,
    Guid? TeamId,
    Guid? OpportunityId,
    string? ReviewedByName,
    DateTime? ReviewedAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed class AdminFlyerImportForm
{
    public IFormFile? FlyerFile { get; set; }

    [MaxLength(40)]
    public string? SourcePlatform { get; set; }

    [MaxLength(2000)]
    public string? SourceUrl { get; set; }

    [MaxLength(2000)]
    public string? OriginalExternalImageUrl { get; set; }

    public Guid? SportId { get; set; }

    [MaxLength(100)]
    public string? SportName { get; set; }

    [MaxLength(50)]
    public string? OpportunityType { get; set; }

    [MaxLength(300)]
    public string? Title { get; set; }

    [MaxLength(200)]
    public string? TeamName { get; set; }

    [MaxLength(200)]
    public string? OrganizationName { get; set; }

    [MaxLength(50)]
    public string? AgeGroup { get; set; }

    [MaxLength(100)]
    public string? CompetitionLevel { get; set; }

    public DateTime? EventDate { get; set; }

    public DateTime? EventEndDate { get; set; }

    public DateTime? RegistrationDeadline { get; set; }

    [Range(0, 100000)]
    public decimal? RegistrationFee { get; set; }

    [MaxLength(500)]
    public string? Location { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(2)]
    public string? State { get; set; }

    [MaxLength(10)]
    public string? ZipCode { get; set; }

    [MaxLength(255)]
    [EmailAddress]
    public string? ContactEmail { get; set; }

    [MaxLength(20)]
    public string? ContactPhone { get; set; }

    [MaxLength(500)]
    public string? WebsiteUrl { get; set; }

    [MaxLength(4000)]
    public string? Description { get; set; }

    [MaxLength(1000)]
    public string? RequiredEquipment { get; set; }

    [MaxLength(1000)]
    public string? WhatToBring { get; set; }

    [MaxLength(2000)]
    public string? SpecialInstructions { get; set; }

    public string? ExtractedJson { get; set; }

    public string? ConfidenceJson { get; set; }

    [MaxLength(2000)]
    public string? AdminNotes { get; set; }
}

public sealed class AdminCreateListingFromFlyerImportForm
{
    public bool PublishImmediately { get; set; }

    public bool ConfirmDuplicateOverride { get; set; }
}

public sealed class AdminRejectFlyerImportForm
{
    [MaxLength(2000)]
    public string? AdminNotes { get; set; }
}

public sealed record AdminSportOption(Guid SportId, string Name);

public class CreateFlyerImportRequest
{
    public string? SourcePlatform { get; set; }

    public string? SourceUrl { get; set; }

    public string? OriginalExternalImageUrl { get; set; }

    public Guid? SportId { get; set; }

    public string? SportName { get; set; }

    public string? OpportunityType { get; set; }

    public string? Title { get; set; }

    public string? TeamName { get; set; }

    public string? OrganizationName { get; set; }

    public string? AgeGroup { get; set; }

    public string? CompetitionLevel { get; set; }

    public DateTime? EventDate { get; set; }

    public DateTime? EventEndDate { get; set; }

    public DateTime? RegistrationDeadline { get; set; }

    public decimal? RegistrationFee { get; set; }

    public string? Location { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? ZipCode { get; set; }

    public string? ContactEmail { get; set; }

    public string? ContactPhone { get; set; }

    public string? WebsiteUrl { get; set; }

    public string? Description { get; set; }

    public string? RequiredEquipment { get; set; }

    public string? WhatToBring { get; set; }

    public string? SpecialInstructions { get; set; }

    public string? ExtractedJson { get; set; }

    public string? ConfidenceJson { get; set; }

    public string? AdminNotes { get; set; }
}

public sealed class CreateFlyerImportUploadRequest : CreateFlyerImportRequest
{
    public IFormFile? FlyerFile { get; set; }
}

public sealed class CreateListingFromFlyerImportRequest
{
    public bool PublishImmediately { get; set; }

    public bool ConfirmDuplicateOverride { get; set; }
}

public sealed record FlyerImportListResponse(
    IReadOnlyCollection<FlyerImportResponse> Imports,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record FlyerImportResponse(
    Guid ImportId,
    string Status,
    string SourcePlatform,
    string? SourceUrl,
    string? OriginalExternalImageUrl,
    bool HasStoredFlyer,
    string? StoredFileName,
    string? StoredContentType,
    string? Title,
    string? TeamName,
    string? OrganizationName,
    Guid? SportId,
    string? SportName,
    string? OpportunityType,
    string? AgeGroup,
    string? CompetitionLevel,
    DateTime? EventDateUtc,
    DateTime? EventEndDateUtc,
    DateTime? RegistrationDeadlineUtc,
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
    string? AdminNotes,
    Guid? TeamId,
    Guid? OpportunityId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record FlyerImportActionResponse(
    string Message,
    FlyerImportResponse FlyerImport,
    Guid? TeamId = null,
    Guid? OpportunityId = null);
