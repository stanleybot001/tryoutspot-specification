using TryOutSpot.Web.Models.Account;

namespace TryOutSpot.Web.Models.SocialLogin;

public sealed record SocialLoginProviderResponse(
    string Provider,
    string DisplayName,
    bool IsConfigured,
    string? ChallengeUrl);

public sealed record SocialLoginRegistrationRequiredResponse(
    string Message,
    string Provider,
    string Email,
    bool EmailVerified,
    string? FirstName,
    string? LastName,
    string ExternalLoginToken,
    IReadOnlyCollection<AccountRoleResponse> AvailableAccountTypes);

public sealed record LinkedSocialLoginResponse(
    string Provider,
    string ProviderDisplayName);

public sealed record SocialLoginActionResponse(string Message);
