namespace TryOutSpot.Web.Models.UserManagement;

public sealed record ManagedUserSummaryResponse(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    IReadOnlyCollection<string> AccountTypes,
    bool IsActive,
    bool EmailConfirmed,
    bool PhoneNumberConfirmed,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ManagedUserDetailResponse(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    DateTime? DateOfBirth,
    string? ZipCode,
    string? City,
    string? State,
    IReadOnlyCollection<string> AccountTypes,
    bool IsActive,
    bool EmailConfirmed,
    bool PhoneNumberConfirmed,
    bool LockoutEnabled,
    DateTimeOffset? LockoutEnd,
    int AccessFailedCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ManagedUserListResponse(
    IReadOnlyCollection<ManagedUserSummaryResponse> Users,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record ManagedUserProfileResponse(
    ManagedUserDetailResponse User,
    IReadOnlyCollection<ManagedUserPlayerProfileSummaryResponse> PlayerProfiles,
    IReadOnlyCollection<ManagedUserPlayerListingSummaryResponse> PlayerListings,
    IReadOnlyCollection<ManagedUserTeamProfileSummaryResponse> Teams,
    IReadOnlyCollection<ManagedUserTeamOpportunitySummaryResponse> TeamOpportunities);

public sealed record ManagedUserPlayerProfileSummaryResponse(
    Guid PlayerId,
    string FirstName,
    string LastName,
    DateTime DateOfBirth,
    string? City,
    string? State,
    string? ZipCode,
    string Relationship,
    bool CanManage,
    bool IsSearchable,
    bool IsActive,
    IReadOnlyCollection<string> Sports);

public sealed record ManagedUserPlayerListingSummaryResponse(
    Guid ListingId,
    string ListingType,
    string Title,
    string? PlayerName,
    string? SportName,
    bool IsPublished,
    bool IsSearchable,
    bool IsActive,
    int OpenReportCount,
    int TotalReportCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ManagedUserTeamProfileSummaryResponse(
    Guid TeamId,
    string TeamName,
    string? OrganizationName,
    string Role,
    string GeographicScope,
    string? TeamLevel,
    string? City,
    string? State,
    string? ZipCode,
    bool IsSearchable,
    bool IsContactInfoVisible,
    bool IsActive,
    IReadOnlyCollection<string> Sports);

public sealed record ManagedUserTeamOpportunitySummaryResponse(
    Guid OpportunityId,
    Guid TeamId,
    string TeamName,
    string Type,
    string Title,
    string SportName,
    bool IsPublished,
    bool IsActive,
    int OpenReportCount,
    int TotalReportCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record ManagedUserActionResponse(string Message);
