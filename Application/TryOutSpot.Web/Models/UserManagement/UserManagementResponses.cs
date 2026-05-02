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

public sealed record ManagedUserActionResponse(string Message);
