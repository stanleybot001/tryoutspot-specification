using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Models.Account;
using TryOutSpot.Web.Security;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Controllers;

/// <summary>
/// Account registration, password recovery, and account lifecycle endpoints.
/// </summary>
[ApiController]
[Tags("Account")]
[Produces("application/json")]
[Route("api/account")]
[EnableRateLimiting(TryOutSpotRateLimitPolicies.AccountSecurity)]
public sealed class AccountApiController(
    AppDbContext dbContext,
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    IAccountEmailSender accountEmailSender,
    IAccountSmsSender accountSmsSender,
    IAuthTokenService authTokenService) : ControllerBase
{
    /// <summary>
    /// Creates a new active user account with one or more account types.
    /// </summary>
    /// <remarks>
    /// Account types are additive. For example, the same user can register as both Parent and Coach.
    /// </remarks>
    /// <response code="201">Returns the created user account summary.</response>
    /// <response code="400">The request failed validation or Identity password rules.</response>
    [HttpPost("register")]
    [ProducesResponseType<UserAccountResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserAccountResponse>> Register(
        RegisterUserRequest request,
        CancellationToken cancellationToken)
    {
        var accountTypes = GetValidatedAccountTypes(request.AccountTypes);
        if (accountTypes.Count == 0)
        {
            return ValidationProblem(ModelState);
        }
        ValidateSmsConsent(request.SmsConsentAccepted, request.PhoneNumber, nameof(request.PhoneNumber));
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var now = DateTime.UtcNow;
        var email = request.Email.Trim();
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim(),
            DateOfBirth = request.DateOfBirth,
            ZipCode = string.IsNullOrWhiteSpace(request.ZipCode) ? null : request.ZipCode.Trim(),
            City = string.IsNullOrWhiteSpace(request.City) ? null : request.City.Trim(),
            State = string.IsNullOrWhiteSpace(request.State) ? null : request.State.Trim().ToUpperInvariant(),
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true,
            LockoutEnabled = true
        };
        ApplySmsConsent(user, request.SmsConsentAccepted, now, TryOutSpotSmsConsent.ApiAccountRegistrationSource);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var createResult = await userManager.CreateAsync(user, request.Password);
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

        await transaction.CommitAsync(cancellationToken);

        var confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
        await accountEmailSender.SendEmailConfirmationTokenAsync(user, confirmationToken, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, ToResponse(user, accountTypes));
    }

    /// <summary>
    /// Authenticates an active, email-verified account and returns bearer and refresh tokens.
    /// </summary>
    /// <response code="200">Returns a bearer access token and refresh token.</response>
    /// <response code="400">The account is not allowed to sign in, usually because email verification is required.</response>
    /// <response code="401">The credentials are invalid.</response>
    /// <response code="423">The account is locked out after repeated failed login attempts.</response>
    [HttpPost("login")]
    [ProducesResponseType<AuthTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status423Locked)]
    public async Task<ActionResult<AuthTokenResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is not { IsActive: true })
        {
            return Unauthorized(new AccountActionResponse("Invalid login attempt."));
        }

        var signInResult = await signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true);

        if (signInResult.IsLockedOut)
        {
            return StatusCode(
                StatusCodes.Status423Locked,
                new AccountActionResponse("The account is temporarily locked. Try again later."));
        }

        if (signInResult.IsNotAllowed)
        {
            return BadRequest(new AccountActionResponse("Email verification is required before login."));
        }

        if (!signInResult.Succeeded)
        {
            return Unauthorized(new AccountActionResponse("Invalid login attempt."));
        }

        var tokenResponse = await authTokenService.CreateTokenResponseAsync(user, cancellationToken);
        return Ok(tokenResponse);
    }

    /// <summary>
    /// Refreshes bearer authentication using a valid refresh token.
    /// </summary>
    /// <response code="200">Returns a new bearer access token and refresh token.</response>
    /// <response code="401">The refresh token is invalid or expired.</response>
    [HttpPost("refresh-token")]
    [ProducesResponseType<AuthTokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthTokenResponse>> RefreshToken(
        TokenRefreshRequest request,
        CancellationToken cancellationToken)
    {
        var tokenResponse = await authTokenService.RefreshTokenAsync(request.RefreshToken, cancellationToken);
        return tokenResponse is null
            ? Unauthorized(new AccountActionResponse("The refresh token is invalid or expired."))
            : Ok(tokenResponse);
    }

    /// <summary>
    /// Logs out by revoking a refresh token. Existing bearer tokens expire naturally.
    /// </summary>
    /// <response code="200">The logout request was accepted.</response>
    [HttpPost("logout")]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountActionResponse>> Logout(
        LogoutRequest request,
        CancellationToken cancellationToken)
    {
        await authTokenService.RevokeRefreshTokenAsync(request.RefreshToken, cancellationToken);
        return Ok(new AccountActionResponse("The account has been logged out."));
    }

    /// <summary>
    /// Returns the authenticated account profile and verification state.
    /// </summary>
    /// <response code="200">Returns the current account.</response>
    /// <response code="401">The bearer token is missing or invalid.</response>
    [Authorize]
    [HttpGet("me")]
    [ProducesResponseType<CurrentUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserResponse>> Me()
    {
        var user = await GetCurrentUserAsync();
        if (user is not { IsActive: true })
        {
            return Unauthorized();
        }

        var roles = await userManager.GetRolesAsync(user);
        return Ok(ToCurrentUserResponse(user, roles.ToArray()));
    }

    /// <summary>
    /// Verifies an account email address using an ASP.NET Core Identity confirmation token.
    /// </summary>
    /// <response code="200">The email address was verified.</response>
    /// <response code="400">The token or account is invalid.</response>
    [HttpPost("verify-email")]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AccountActionResponse>> VerifyEmail(VerifyEmailRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is not { IsActive: true })
        {
            return BadRequest(new AccountActionResponse("The email verification request is invalid or expired."));
        }

        var result = await userManager.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return ValidationProblem(ModelState);
        }

        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        return Ok(new AccountActionResponse("The email address has been verified."));
    }

    /// <summary>
    /// Resends email verification instructions when an active account still needs verification.
    /// </summary>
    /// <remarks>
    /// The response is intentionally generic to avoid revealing whether an email address exists.
    /// </remarks>
    /// <response code="200">Always returns a generic verification message.</response>
    [HttpPost("resend-email-verification")]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountActionResponse>> ResendEmailVerification(
        ResendEmailVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is { IsActive: true, EmailConfirmed: false })
        {
            var confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
            await accountEmailSender.SendEmailConfirmationTokenAsync(user, confirmationToken, cancellationToken);
        }

        return Ok(new AccountActionResponse(
            "If an active unverified account exists for that email address, verification instructions will be sent."));
    }

    /// <summary>
    /// Starts the forgot-password flow for an active account.
    /// </summary>
    /// <remarks>
    /// The response is intentionally generic to avoid revealing whether an email address exists.
    /// </remarks>
    /// <response code="200">Always returns a generic password reset message.</response>
    [HttpPost("forgot-password")]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AccountActionResponse>> ForgotPassword(
        ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is { IsActive: true })
        {
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
            await accountEmailSender.SendPasswordResetTokenAsync(user, resetToken, cancellationToken);
        }

        return Ok(new AccountActionResponse(
            "If an active account exists for that email address, password reset instructions will be sent."));
    }

    /// <summary>
    /// Sends a phone verification code for the authenticated account.
    /// </summary>
    /// <remarks>
    /// The current implementation uses a sender abstraction. The development sender logs the code.
    /// </remarks>
    /// <response code="200">The code was sent.</response>
    /// <response code="400">The account has no phone number to verify.</response>
    /// <response code="401">The bearer token is missing or invalid.</response>
    [Authorize]
    [HttpPost("send-phone-verification")]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AccountActionResponse>> SendPhoneVerification(
        SendPhoneVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserAsync();
        if (user is not { IsActive: true })
        {
            return Unauthorized();
        }

        var phoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
            ? user.PhoneNumber
            : request.PhoneNumber.Trim();

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return BadRequest(new AccountActionResponse("A phone number is required before verification."));
        }
        if (!user.SmsConsentAccepted)
        {
            return BadRequest(new AccountActionResponse(
                "SMS consent is required before sending phone verification messages."));
        }

        var verificationCode = await userManager.GenerateChangePhoneNumberTokenAsync(user, phoneNumber);
        await accountSmsSender.SendPhoneVerificationCodeAsync(user, phoneNumber, verificationCode, cancellationToken);

        return Ok(new AccountActionResponse("The phone verification code has been sent."));
    }

    /// <summary>
    /// Verifies the authenticated account phone number with a code.
    /// </summary>
    /// <response code="200">The phone number was verified.</response>
    /// <response code="400">The code or account phone number is invalid.</response>
    /// <response code="401">The bearer token is missing or invalid.</response>
    [Authorize]
    [HttpPost("verify-phone")]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AccountActionResponse>> VerifyPhone(VerifyPhoneRequest request)
    {
        var user = await GetCurrentUserAsync();
        if (user is not { IsActive: true })
        {
            return Unauthorized();
        }

        var phoneNumber = string.IsNullOrWhiteSpace(request.PhoneNumber)
            ? user.PhoneNumber
            : request.PhoneNumber.Trim();

        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            return BadRequest(new AccountActionResponse("A phone number is required before verification."));
        }

        var result = await userManager.ChangePhoneNumberAsync(user, phoneNumber, request.Code);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return ValidationProblem(ModelState);
        }

        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        return Ok(new AccountActionResponse("The phone number has been verified."));
    }

    /// <summary>
    /// Resets an active account password using an ASP.NET Core Identity reset token.
    /// </summary>
    /// <response code="200">The password was reset.</response>
    /// <response code="400">The token, account, or new password is invalid.</response>
    [HttpPost("reset-password")]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AccountActionResponse>> ResetPassword(ResetPasswordRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is not { IsActive: true })
        {
            return BadRequest(new AccountActionResponse("The password reset request is invalid or expired."));
        }

        var resetResult = await userManager.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!resetResult.Succeeded)
        {
            AddIdentityErrors(resetResult);
            return ValidationProblem(ModelState);
        }

        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        return Ok(new AccountActionResponse("The password has been reset."));
    }

    /// <summary>
    /// Soft deletes an active account after password verification.
    /// </summary>
    /// <remarks>
    /// The user row is retained, marked inactive, locked, and receives a new security stamp.
    /// </remarks>
    /// <response code="200">The account was soft deleted.</response>
    /// <response code="400">The account could not be deleted.</response>
    [HttpPost("delete-account")]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<AccountActionResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AccountActionResponse>> DeleteAccount(DeleteAccountRequest request)
    {
        var user = await userManager.FindByEmailAsync(request.Email.Trim());
        if (user is not { IsActive: true })
        {
            return BadRequest(new AccountActionResponse("The account could not be deleted."));
        }

        if (!await userManager.CheckPasswordAsync(user, request.Password))
        {
            return BadRequest(new AccountActionResponse("The account could not be deleted."));
        }

        user.IsActive = false;
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.UtcNow.AddYears(100);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
        user.UpdatedAt = DateTime.UtcNow;

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            AddIdentityErrors(updateResult);
            return ValidationProblem(ModelState);
        }

        return Ok(new AccountActionResponse("The account has been deleted."));
    }

    /// <summary>
    /// Lists the public account types that can be selected during registration.
    /// </summary>
    /// <response code="200">Returns public registration account types.</response>
    [HttpGet("account-types")]
    [ProducesResponseType<AccountRoleResponse[]>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyCollection<AccountRoleResponse>> GetAccountTypes()
    {
        var roles = TryOutSpotRoles.PublicRegistrationRoles
            .Select(role => new AccountRoleResponse(role))
            .ToArray();

        return Ok(roles);
    }

    private List<string> GetValidatedAccountTypes(IReadOnlyCollection<string>? requestedAccountTypes)
    {
        if (requestedAccountTypes is null || requestedAccountTypes.Count == 0)
        {
            ModelState.AddModelError(
                nameof(RegisterUserRequest.AccountTypes),
                "At least one account type is required.");

            return [];
        }

        var accountTypes = new List<string>();
        foreach (var requestedAccountType in requestedAccountTypes)
        {
            var accountType = TryOutSpotRoles.NormalizePublicRegistrationRole(requestedAccountType);
            if (accountType is null)
            {
                ModelState.AddModelError(
                    nameof(RegisterUserRequest.AccountTypes),
                    $"'{requestedAccountType}' is not a supported account type.");

                continue;
            }

            if (!accountTypes.Contains(accountType, StringComparer.OrdinalIgnoreCase))
            {
                accountTypes.Add(accountType);
            }
        }

        return accountTypes;
    }

    private void AddIdentityErrors(IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(error.Code, error.Description);
        }
    }

    private static UserAccountResponse ToResponse(User user, IReadOnlyCollection<string> accountTypes)
    {
        return new UserAccountResponse(
            user.Id,
            user.Email ?? string.Empty,
            user.FirstName,
            user.LastName,
            accountTypes,
            user.IsActive,
            user.SmsConsentAccepted);
    }

    private async Task<User?> GetCurrentUserAsync()
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userIdClaim, out var userId)
            ? await userManager.FindByIdAsync(userId.ToString())
            : null;
    }

    private static CurrentUserResponse ToCurrentUserResponse(User user, IReadOnlyCollection<string> accountTypes)
    {
        return new CurrentUserResponse(
            user.Id,
            user.Email ?? string.Empty,
            user.FirstName,
            user.LastName,
            accountTypes,
            user.IsActive,
            user.EmailConfirmed,
            user.PhoneNumber,
            user.PhoneNumberConfirmed,
            user.SmsConsentAccepted,
            user.SmsConsentAcceptedAt);
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
}
