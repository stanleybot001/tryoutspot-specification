namespace TryOutSpot.Web.Models.Moderation;

public sealed record AdminListingReportListResponse(
    IReadOnlyCollection<AdminListingReportSummaryResponse> Reports,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record AdminListingReportSummaryResponse(
    Guid ReportId,
    string TargetType,
    Guid TargetId,
    string TargetTitle,
    Guid ReporterUserId,
    string ReporterName,
    string ReporterEmail,
    string Reason,
    string? Details,
    string Status,
    string? AdminNotes,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    Guid? ReviewedByUserId,
    string? ReviewedByUserName,
    DateTime? ReviewedAtUtc);

public sealed record AdminListingReportDetailResponse(
    AdminListingReportSummaryResponse Report,
    AdminUserProfileSummaryResponse Reporter,
    AdminUserProfileSummaryResponse? ReviewedBy,
    AdminPlayerListingDetailResponse? PlayerListing,
    AdminTeamOpportunityDetailResponse? TeamOpportunity);

public sealed record AdminListingListResponse(
    IReadOnlyCollection<AdminListingSummaryResponse> Listings,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record AdminListingSummaryResponse(
    string TargetType,
    Guid TargetId,
    string Title,
    string? ListingType,
    string? SportName,
    Guid? OwnerUserId,
    string? OwnerName,
    string? OwnerEmail,
    Guid? TeamId,
    string? TeamName,
    bool IsPublished,
    bool IsSearchable,
    bool IsActive,
    int OpenReportCount,
    int TotalReportCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record AdminPlayerListingDetailResponse(
    Guid Id,
    Guid UserId,
    string OwnerName,
    string OwnerEmail,
    string ListingType,
    string Title,
    string? Description,
    Guid? PlayerId,
    string? PlayerName,
    string? SportName,
    string? City,
    string? State,
    string? ZipCode,
    bool IsPublished,
    bool IsSearchable,
    bool IsActive,
    int OpenReportCount,
    int TotalReportCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record AdminTeamOpportunityDetailResponse(
    Guid Id,
    Guid TeamId,
    string TeamName,
    string? OrganizationName,
    string Type,
    string Title,
    string? Description,
    string SportName,
    string? City,
    string? State,
    string? ZipCode,
    bool IsPublished,
    bool IsActive,
    int OpenReportCount,
    int TotalReportCount,
    IReadOnlyCollection<AdminUserProfileSummaryResponse> TeamRepresentatives,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record AdminUserProfileSummaryResponse(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    string DisplayName,
    bool IsActive);
