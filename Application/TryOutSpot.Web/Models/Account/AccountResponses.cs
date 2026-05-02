namespace TryOutSpot.Web.Models.Account;

/// <summary>
/// Public account type option available during registration.
/// </summary>
public sealed record AccountRoleResponse(string Name);

/// <summary>
/// Summary returned after account creation.
/// </summary>
public sealed record UserAccountResponse(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    IReadOnlyCollection<string> AccountTypes,
    bool IsActive);

/// <summary>
/// Token pair returned after login or refresh.
/// </summary>
public sealed record AuthTokenResponse(
    string TokenType,
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTime RefreshTokenExpiresAtUtc,
    UserAccountResponse User);

/// <summary>
/// Authenticated account profile and verification state.
/// </summary>
public sealed record CurrentUserResponse(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    IReadOnlyCollection<string> AccountTypes,
    bool IsActive,
    bool EmailConfirmed,
    string? PhoneNumber,
    bool PhoneNumberConfirmed);

/// <summary>
/// Standard response for account lifecycle actions.
/// </summary>
public sealed record AccountActionResponse(string Message);
