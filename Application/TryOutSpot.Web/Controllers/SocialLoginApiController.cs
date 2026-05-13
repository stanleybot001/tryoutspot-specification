using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Models.Account;
using TryOutSpot.Web.Models.SocialLogin;
using TryOutSpot.Web.Security;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Controllers;

/// <summary>
/// External/social login endpoints for Google, Facebook, and future Apple sign-in.
/// </summary>
[ApiController]
[Tags("Social Login")]
[Produces("application/json")]
[Route("api/social-login")]
public sealed class SocialLoginApiController(
    AppDbContext dbContext,
    IConfiguration configuration,
    UserManager<User> userManager,
    IAuthTokenService authTokenService,
    IAccountEmailSender accountEmailSender,
    IExternalLoginTicketService externalLoginTicketService,
    IOptions<GoogleAuthenticationOptions> googleOptions) : ControllerBase
{
    private readonly GoogleAuthenticationOptions googleAuthentication = googleOptions.Value;

    /// <summary>
    /// Lists social login providers and whether each one is configured.
    /// </summary>
    [HttpGet("providers")]
    [ProducesResponseType<SocialLoginProviderResponse[]>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyCollection<SocialLoginProviderResponse>> GetProviders()
    {
        var providers = TryOutSpotSocialLoginProviders.All
            .Select(provider => new SocialLoginProviderResponse(
                provider,
                GetProviderDisplayName(provider),
                IsProviderConfigured(provider),
                IsProviderConfigured(provider) ? Url.Action(nameof(ChallengeProvider), new { provider }) : null))
            .ToArray();

        return Ok(providers);
    }

    /// <summary>
    /// Starts an external login challenge for a configured provider.
    /// </summary>
    [HttpGet("challenge/{provider}")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public IActionResult ChallengeProvider(string provider)
    {
        var normalizedProvider = TryOutSpotSocialLoginProviders.Normalize(provider);
        if (normalizedProvider is null)
        {
            ModelState.AddModelError(nameof(provider), $"'{provider}' is not a supported social login provider.");
            return ValidationProblem(ModelState);
        }

        if (!IsProviderConfigured(normalizedProvider))
        {
            ModelState.AddModelError(nameof(provider), $"{normalizedProvider} social login is not configured.");
            return ValidationProblem(ModelState);
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(Callback), new { provider = normalizedProvider })
        };
        properties.Items["LoginProvider"] = normalizedProvider;

        return Challenge(properties, normalizedProvider);
    }

    /// <summary>
    /// Completes an external provider callback.
    /// </summary>
    /// <remarks>
    /// Existing linked users receive JWT tokens. New social identities receive an external login token
    /// that can be submitted to the register or link endpoints.
    /// </remarks>
    [HttpGet("callback")]
    [ProducesResponseType<AuthTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<SocialLoginRegistrationRequiredResponse>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> Callback(
        [FromQuery] string? provider,
        CancellationToken cancellationToken)
    {
        var authenticateResult = await HttpContext.AuthenticateAsync(IdentityConstants.ExternalScheme);
        if (!authenticateResult.Succeeded || authenticateResult.Principal is null)
        {
            ModelState.AddModelError(nameof(provider), "The social login callback could not be authenticated.");
            return ValidationProblem(ModelState);
        }

        var normalizedProvider = TryOutSpotSocialLoginProviders.Normalize(
            authenticateResult.Properties?.Items.TryGetValue("LoginProvider", out var loginProvider) == true
                ? loginProvider
                : provider);

        if (normalizedProvider is null)
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            ModelState.AddModelError(nameof(provider), "The social login provider is invalid.");
            return ValidationProblem(ModelState);
        }

        var principal = authenticateResult.Principal;
        var providerKey = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(providerKey) || string.IsNullOrWhiteSpace(email))
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            ModelState.AddModelError(nameof(provider), "The social login provider did not return the required identity data.");
            return ValidationProblem(ModelState);
        }

        var ticket = CreateTicket(normalizedProvider, providerKey, email, principal);
        var linkedUser = await userManager.FindByLoginAsync(normalizedProvider, providerKey);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        if (linkedUser is not null)
        {
            if (!linkedUser.IsActive)
            {
                return Unauthorized(new AccountActionResponse("The linked account is inactive."));
            }

            if (ApplyVerifiedExternalEmail(linkedUser, ticket, DateTime.UtcNow))
            {
                await userManager.UpdateAsync(linkedUser);
            }

            var tokenResponse = await authTokenService.CreateTokenResponseAsync(linkedUser, cancellationToken);
            return Ok(tokenResponse);
        }

        var externalLoginToken = externalLoginTicketService.Create(ticket);
        return Conflict(new SocialLoginRegistrationRequiredResponse(
            "Complete registration or link this social login to an existing account.",
            normalizedProvider,
            ticket.Email,
            ticket.EmailVerified,
            ticket.FirstName,
            ticket.LastName,
            ticket.ProfileImageUrl,
            externalLoginToken,
            GetPublicAccountTypes()));
    }

    /// <summary>
    /// Creates a new user from a verified external login token.
    /// </summary>
    [HttpPost("register")]
    [ProducesResponseType<AuthTokenResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AuthTokenResponse>> Register(
        SocialLoginRegisterRequest request,
        CancellationToken cancellationToken)
    {
        if (!externalLoginTicketService.TryRead(request.ExternalLoginToken, out var ticket))
        {
            ModelState.AddModelError(nameof(request.ExternalLoginToken), "The external login token is invalid or expired.");
            return ValidationProblem(ModelState);
        }

        var accountTypes = GetValidatedPublicAccountTypes(request.AccountTypes, nameof(request.AccountTypes));
        if (accountTypes.Count == 0)
        {
            return ValidationProblem(ModelState);
        }
        ValidateSmsConsent(request.SmsConsentAccepted, request.PhoneNumber, nameof(request.PhoneNumber));
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var existingLogin = await userManager.FindByLoginAsync(ticket.Provider, ticket.ProviderKey);
        if (existingLogin is not null)
        {
            ModelState.AddModelError(nameof(request.ExternalLoginToken), "This social login is already linked to an account.");
            return ValidationProblem(ModelState);
        }

        var existingEmailUser = await userManager.FindByEmailAsync(ticket.Email);
        if (existingEmailUser is not null)
        {
            ModelState.AddModelError(
                nameof(request.ExternalLoginToken),
                "An account with this email already exists. Sign in and link the social login instead.");
            return ValidationProblem(ModelState);
        }

        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = ticket.Email,
            Email = ticket.Email,
            EmailConfirmed = ticket.EmailVerified,
            FirstName = NormalizeRequiredName(request.FirstName, ticket.FirstName, "Social"),
            LastName = NormalizeRequiredName(request.LastName, ticket.LastName, "User"),
            PhoneNumber = NormalizeOptional(request.PhoneNumber),
            DateOfBirth = request.DateOfBirth,
            ZipCode = NormalizeOptional(request.ZipCode),
            City = NormalizeOptional(request.City),
            State = NormalizeState(request.State),
            ProfileImageUrl = NormalizeOptional(ticket.ProfileImageUrl),
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true,
            LockoutEnabled = true
        };
        ApplySmsConsent(user, request.SmsConsentAccepted, now, TryOutSpotSmsConsent.ApiSocialRegistrationSource);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var createResult = await userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            AddIdentityErrors(createResult);
            return ValidationProblem(ModelState);
        }

        var roleResult = await userManager.AddToRolesAsync(user, accountTypes);
        if (!roleResult.Succeeded)
        {
            AddIdentityErrors(roleResult);
            return ValidationProblem(ModelState);
        }

        var loginResult = await userManager.AddLoginAsync(
            user,
            new UserLoginInfo(ticket.Provider, ticket.ProviderKey, GetProviderDisplayName(ticket.Provider)));
        if (!loginResult.Succeeded)
        {
            AddIdentityErrors(loginResult);
            return ValidationProblem(ModelState);
        }

        await transaction.CommitAsync(cancellationToken);

        if (!user.EmailConfirmed)
        {
            var confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
            await accountEmailSender.SendEmailConfirmationTokenAsync(user, confirmationToken, cancellationToken);
        }

        var tokenResponse = await authTokenService.CreateTokenResponseAsync(user, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, tokenResponse);
    }

    /// <summary>
    /// Lists social logins linked to the authenticated account.
    /// </summary>
    [Authorize]
    [HttpGet("linked")]
    [ProducesResponseType<LinkedSocialLoginResponse[]>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyCollection<LinkedSocialLoginResponse>>> GetLinkedLogins()
    {
        var user = await GetCurrentUserAsync();
        if (user is not { IsActive: true })
        {
            return Unauthorized();
        }

        var logins = await userManager.GetLoginsAsync(user);
        return Ok(logins
            .Select(login => new LinkedSocialLoginResponse(
                login.LoginProvider,
                string.IsNullOrWhiteSpace(login.ProviderDisplayName)
                    ? GetProviderDisplayName(login.LoginProvider)
                    : login.ProviderDisplayName))
            .OrderBy(login => login.Provider)
            .ToArray());
    }

    /// <summary>
    /// Links a social login to the authenticated account.
    /// </summary>
    [Authorize]
    [HttpPost("link")]
    [ProducesResponseType<SocialLoginActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SocialLoginActionResponse>> Link(
        SocialLoginLinkRequest request)
    {
        var user = await GetCurrentUserAsync();
        if (user is not { IsActive: true })
        {
            return Unauthorized();
        }

        if (!externalLoginTicketService.TryRead(request.ExternalLoginToken, out var ticket))
        {
            ModelState.AddModelError(nameof(request.ExternalLoginToken), "The external login token is invalid or expired.");
            return ValidationProblem(ModelState);
        }

        var existingLogin = await userManager.FindByLoginAsync(ticket.Provider, ticket.ProviderKey);
        if (existingLogin is not null && existingLogin.Id != user.Id)
        {
            ModelState.AddModelError(nameof(request.ExternalLoginToken), "This social login is already linked to another account.");
            return ValidationProblem(ModelState);
        }

        if (existingLogin is not null)
        {
            if (ApplyVerifiedExternalEmail(user, ticket, DateTime.UtcNow))
            {
                await userManager.UpdateAsync(user);
            }

            return Ok(new SocialLoginActionResponse("The social login is already linked to this account."));
        }

        var loginResult = await userManager.AddLoginAsync(
            user,
            new UserLoginInfo(ticket.Provider, ticket.ProviderKey, GetProviderDisplayName(ticket.Provider)));
        if (!loginResult.Succeeded)
        {
            AddIdentityErrors(loginResult);
            return ValidationProblem(ModelState);
        }

        var now = DateTime.UtcNow;
        user.UpdatedAt = now;
        ApplyVerifiedExternalEmail(user, ticket, now);
        await userManager.UpdateAsync(user);

        return Ok(new SocialLoginActionResponse("The social login has been linked."));
    }

    /// <summary>
    /// Unlinks a social login from the authenticated account.
    /// </summary>
    [Authorize]
    [HttpPost("unlink")]
    [ProducesResponseType<SocialLoginActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<SocialLoginActionResponse>> Unlink(
        SocialLoginUnlinkRequest request)
    {
        var user = await GetCurrentUserAsync();
        if (user is not { IsActive: true })
        {
            return Unauthorized();
        }

        var provider = TryOutSpotSocialLoginProviders.Normalize(request.Provider);
        if (provider is null)
        {
            ModelState.AddModelError(nameof(request.Provider), $"'{request.Provider}' is not a supported social login provider.");
            return ValidationProblem(ModelState);
        }

        var logins = await userManager.GetLoginsAsync(user);
        var login = logins.SingleOrDefault(currentLogin => currentLogin.LoginProvider == provider);
        if (login is null)
        {
            return Ok(new SocialLoginActionResponse("The social login was not linked to this account."));
        }

        if (string.IsNullOrWhiteSpace(user.PasswordHash) && logins.Count <= 1)
        {
            ModelState.AddModelError(
                nameof(request.Provider),
                "Add a password or another social login before removing the only sign-in method.");
            return ValidationProblem(ModelState);
        }

        var removeResult = await userManager.RemoveLoginAsync(user, login.LoginProvider, login.ProviderKey);
        if (!removeResult.Succeeded)
        {
            AddIdentityErrors(removeResult);
            return ValidationProblem(ModelState);
        }

        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        return Ok(new SocialLoginActionResponse("The social login has been unlinked."));
    }

    private ExternalLoginTicket CreateTicket(
        string provider,
        string providerKey,
        string email,
        ClaimsPrincipal principal)
    {
        return new ExternalLoginTicket(
            provider,
            providerKey,
            email.Trim(),
            IsEmailVerified(provider, principal),
            principal.FindFirstValue(ClaimTypes.GivenName),
            principal.FindFirstValue(ClaimTypes.Surname),
            principal.FindFirstValue("urn:google:picture"));
    }

    private bool IsProviderConfigured(string provider)
    {
        return provider switch
        {
            TryOutSpotSocialLoginProviders.Google => googleAuthentication.IsConfigured,
            TryOutSpotSocialLoginProviders.Facebook => HasConfiguredValue(configuration["Authentication:Facebook:AppId"])
                && HasConfiguredValue(configuration["Authentication:Facebook:AppSecret"]),
            TryOutSpotSocialLoginProviders.Apple => false,
            _ => false
        };
    }

    private static bool IsEmailVerified(string provider, ClaimsPrincipal principal)
    {
        if (TryReadBooleanClaim(
                principal,
                out var isVerified,
                "urn:google:email_verified",
                "urn:facebook:email_verified",
                "urn:apple:email_verified",
                "email_verified",
                "verified_email"))
        {
            return isVerified;
        }

        return provider is TryOutSpotSocialLoginProviders.Google
            or TryOutSpotSocialLoginProviders.Facebook
            or TryOutSpotSocialLoginProviders.Apple;
    }

    private static bool TryReadBooleanClaim(
        ClaimsPrincipal principal,
        out bool value,
        params string[] claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var rawValue = principal.FindFirstValue(claimType);
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                continue;
            }

            if (bool.TryParse(rawValue, out value))
            {
                return true;
            }

            if (string.Equals(rawValue, "1", StringComparison.Ordinal))
            {
                value = true;
                return true;
            }

            if (string.Equals(rawValue, "0", StringComparison.Ordinal))
            {
                value = false;
                return true;
            }
        }

        value = false;
        return false;
    }

    private static bool ApplyVerifiedExternalEmail(
        User user,
        ExternalLoginTicket ticket,
        DateTime updatedAtUtc)
    {
        if (!ticket.EmailVerified || user.EmailConfirmed)
        {
            return false;
        }

        if (!string.Equals(user.Email, ticket.Email, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        user.EmailConfirmed = true;
        user.UpdatedAt = updatedAtUtc;
        return true;
    }

    private List<string> GetValidatedPublicAccountTypes(
        IReadOnlyCollection<string>? requestedAccountTypes,
        string modelStateKey)
    {
        if (requestedAccountTypes is null || requestedAccountTypes.Count == 0)
        {
            ModelState.AddModelError(modelStateKey, "At least one account type is required.");
            return [];
        }

        var accountTypes = new List<string>();
        foreach (var requestedAccountType in requestedAccountTypes)
        {
            var accountType = TryOutSpotRoles.NormalizePublicRegistrationRole(requestedAccountType);
            if (accountType is null)
            {
                ModelState.AddModelError(
                    modelStateKey,
                    $"'{requestedAccountType}' is not a supported public account type.");

                continue;
            }

            if (!accountTypes.Contains(accountType, StringComparer.OrdinalIgnoreCase))
            {
                accountTypes.Add(accountType);
            }
        }

        return accountTypes;
    }

    private static IReadOnlyCollection<AccountRoleResponse> GetPublicAccountTypes()
    {
        return TryOutSpotRoles.PublicRegistrationRoles
            .Select(role => new AccountRoleResponse(role))
            .ToArray();
    }

    private async Task<User?> GetCurrentUserAsync()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdClaim, out var userId)
            ? await userManager.FindByIdAsync(userId.ToString())
            : null;
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(error.Code, error.Description);
        }
    }

    private void ValidateSmsConsent(bool smsConsentAccepted, string? phoneNumber, string modelStateKey)
    {
        if (smsConsentAccepted && string.IsNullOrWhiteSpace(phoneNumber))
        {
            ModelState.AddModelError(
                modelStateKey,
                "Enter a phone number to opt in to transactional SMS messages.");
        }
    }

    private static void ApplySmsConsent(
        User user,
        bool smsConsentAccepted,
        DateTime acceptedAtUtc,
        string source)
    {
        if (!smsConsentAccepted)
        {
            return;
        }

        user.SmsConsentAccepted = true;
        user.SmsConsentAcceptedAt = acceptedAtUtc;
        user.SmsConsentText = TryOutSpotSmsConsent.CheckboxText;
        user.SmsConsentSource = source;
    }

    private static string GetProviderDisplayName(string provider)
    {
        return provider switch
        {
            TryOutSpotSocialLoginProviders.Google => "Google",
            TryOutSpotSocialLoginProviders.Facebook => "Facebook",
            TryOutSpotSocialLoginProviders.Apple => "Apple",
            _ => provider
        };
    }

    private static bool HasConfiguredValue(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !value.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("PUT_", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRequiredName(string? requestValue, string? ticketValue, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(requestValue))
        {
            return requestValue.Trim();
        }

        return string.IsNullOrWhiteSpace(ticketValue) ? fallback : ticketValue.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeState(string? state)
    {
        return string.IsNullOrWhiteSpace(state) ? null : state.Trim().ToUpperInvariant();
    }
}
