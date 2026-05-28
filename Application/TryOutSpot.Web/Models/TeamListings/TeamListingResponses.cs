namespace TryOutSpot.Web.Models.TeamListings;

/// <summary>
/// Team list for the signed-in manager account.
/// </summary>
public sealed record ManagedTeamListResponse(
    IReadOnlyCollection<ManagedTeamSummaryResponse> Teams);

/// <summary>
/// Managed team summary.
/// </summary>
public sealed record ManagedTeamSummaryResponse(
    Guid TeamId,
    string TeamName,
    string? OrganizationName,
    string Role,
    string? TeamLevel,
    string GeographicScope,
    string? City,
    string? State,
    string? ZipCode,
    bool IsSearchable,
    bool IsContactInfoVisible,
    int ActiveOpportunityCount,
    int PublishedOpportunityCount,
    IReadOnlyCollection<string> Sports);

/// <summary>
/// Paginated list of opportunities for a managed team.
/// </summary>
public sealed record TeamOpportunityListResponse(
    Guid TeamId,
    IReadOnlyCollection<TeamOpportunitySummaryResponse> Opportunities,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

/// <summary>
/// Team opportunity summary.
/// </summary>
public sealed record TeamOpportunitySummaryResponse(
    Guid Id,
    Guid TeamId,
    Guid SportId,
    string? SportName,
    string Type,
    string Title,
    string? Description,
    string? CompetitionLevel,
    string? AgeGroup,
    bool RegistrationRequired,
    decimal RegistrationFee,
    int? MaxParticipants,
    IReadOnlyCollection<string> RequiredRegistrationFieldCodes,
    bool WaiverRequired,
    string? WaiverMethod,
    bool WaiverReturnByEmail,
    bool WaiverReturnInPerson,
    DateTime? RegistrationDeadline,
    DateTime? EventDate,
    DateTime? EventEndDate,
    DateTime? ListingStartDate,
    DateTime? ListingEndDate,
    string? City,
    string? State,
    string? ZipCode,
    string? WebsiteUrl,
    string? PdfUrl,
    bool IsPublished,
    DateTime? PublishedAt,
    DateTime? ExpiresAt,
    int FavoriteCount,
    DateTime UpdatedAt);

/// <summary>
/// Single managed team opportunity detail.
/// </summary>
public sealed record TeamOpportunityDetailResponse(
    Guid Id,
    Guid TeamId,
    Guid SportId,
    string? SportName,
    string Type,
    string Title,
    string? Description,
    string? CompetitionLevel,
    string? AgeGroup,
    bool RegistrationRequired,
    DateTime? RegistrationDeadline,
    decimal RegistrationFee,
    int? MaxParticipants,
    IReadOnlyCollection<string> RequiredRegistrationFieldCodes,
    bool WaiverRequired,
    string? WaiverMethod,
    bool WaiverReturnByEmail,
    bool WaiverReturnInPerson,
    DateTime? EventDate,
    DateTime? EventEndDate,
    DateTime? ListingStartDate,
    DateTime? ListingEndDate,
    string? Location,
    string? Address,
    string? City,
    string? State,
    string? ZipCode,
    string? ContactEmail,
    string? ContactPhone,
    string? WebsiteUrl,
    string? PdfUrl,
    string? WaiverPdfUrl,
    string? RequiredEquipment,
    string? WhatToBring,
    string? SpecialInstructions,
    bool IsPublished,
    DateTime? PublishedAt,
    DateTime? ExpiresAt,
    int FavoriteCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>
/// Simple action response for team opportunity writes.
/// </summary>
public sealed record TeamOpportunityActionResponse(
    string Message,
    TeamOpportunityDetailResponse Opportunity);
