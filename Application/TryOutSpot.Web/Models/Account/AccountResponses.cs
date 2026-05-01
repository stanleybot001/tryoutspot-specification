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
/// Standard response for account lifecycle actions.
/// </summary>
public sealed record AccountActionResponse(string Message);
