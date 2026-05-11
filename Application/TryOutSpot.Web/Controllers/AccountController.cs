using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Models.Billing;
using TryOutSpot.Web.Models.WebAccount;
using TryOutSpot.Web.Security;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Controllers;

[Route("account")]
[AutoValidateAntiforgeryToken]
public sealed class AccountController(
    AppDbContext dbContext,
    UserManager<User> userManager,
    SignInManager<User> signInManager,
    IAccountEmailSender accountEmailSender,
    IAccountSmsSender accountSmsSender,
    IEntitlementService entitlementService,
    IExternalLoginTicketService externalLoginTicketService,
    IOptions<GoogleAuthenticationOptions> googleOptions) : Controller
{
    private readonly GoogleAuthenticationOptions googleAuthentication = googleOptions.Value;

    [HttpGet("register")]
    public IActionResult Register([FromQuery] string? returnUrl = null)
    {
        return View(PrepareRegisterModel(new RegisterPageModel { ReturnUrl = returnUrl }));
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterPageModel model, CancellationToken cancellationToken)
    {
        var accountTypes = GetValidatedPublicAccountTypes(model.AccountTypes, nameof(model.AccountTypes));
        if (accountTypes.Count == 0)
        {
            ModelState.AddModelError(nameof(model.AccountTypes), "Choose at least one account type.");
        }
        ValidateSmsConsent(model.SmsConsentAccepted, model.PhoneNumber, nameof(model.PhoneNumber));

        if (!ModelState.IsValid)
        {
            return View(PrepareRegisterModel(model));
        }

        var now = DateTime.UtcNow;
        var email = model.Email.Trim();
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            FirstName = model.FirstName.Trim(),
            LastName = model.LastName.Trim(),
            PhoneNumber = NormalizeOptional(model.PhoneNumber),
            DateOfBirth = model.DateOfBirth,
            ZipCode = NormalizeOptional(model.ZipCode),
            City = NormalizeOptional(model.City),
            State = NormalizeState(model.State),
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true,
            LockoutEnabled = true
        };
        ApplySmsConsent(user, model.SmsConsentAccepted, now, TryOutSpotSmsConsent.AccountRegistrationSource);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var createResult = await userManager.CreateAsync(user, model.Password);
        if (!createResult.Succeeded)
        {
            AddIdentityErrors(createResult);
            return View(PrepareRegisterModel(model));
        }

        var roleResult = await userManager.AddToRolesAsync(user, accountTypes);
        if (!roleResult.Succeeded)
        {
            AddIdentityErrors(roleResult);
            return View(PrepareRegisterModel(model));
        }

        await transaction.CommitAsync(cancellationToken);

        var confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
        await accountEmailSender.SendEmailConfirmationTokenAsync(user, confirmationToken, cancellationToken);

        TempData["StatusMessage"] = "Account created. Check your email to verify your address before signing in.";
        return RedirectToAction(nameof(RegisterConfirmation), new { email = user.Email });
    }

    [HttpGet("register-confirmation")]
    public IActionResult RegisterConfirmation([FromQuery] string? email)
    {
        ViewData["Email"] = email;
        return View();
    }

    [HttpGet("login")]
    public IActionResult Login([FromQuery] string? returnUrl = null)
    {
        return View(new LoginPageModel
        {
            ReturnUrl = returnUrl,
            GoogleIsConfigured = googleAuthentication.IsConfigured
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginPageModel model, CancellationToken cancellationToken)
    {
        model.GoogleIsConfigured = googleAuthentication.IsConfigured;
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email.Trim());
        if (user is not { IsActive: true })
        {
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return View(model);
        }

        var signInResult = await signInManager.CheckPasswordSignInAsync(
            user,
            model.Password,
            lockoutOnFailure: true);

        if (signInResult.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "The account is temporarily locked. Try again later.");
            return View(model);
        }

        if (signInResult.IsNotAllowed)
        {
            ModelState.AddModelError(string.Empty, "Email verification is required before login.");
            return View(model);
        }

        if (!signInResult.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return View(model);
        }

        await SignInWebUserAsync(user, model.RememberMe);

        if (!string.IsNullOrWhiteSpace(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
        {
            return LocalRedirect(model.ReturnUrl);
        }

        return RedirectToAction(nameof(Onboarding));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(TryOutSpotAuthenticationSchemes.WebCookie);
        TempData["StatusMessage"] = "You have been signed out.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet("forgot-password")]
    public IActionResult ForgotPassword()
    {
        return View(new ForgotPasswordPageModel());
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword(
        ForgotPasswordPageModel model,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email.Trim());
        if (user is { IsActive: true })
        {
            var resetToken = await userManager.GeneratePasswordResetTokenAsync(user);
            await accountEmailSender.SendPasswordResetTokenAsync(user, resetToken, cancellationToken);
        }

        TempData["StatusMessage"] = "If an active account exists for that email address, password reset instructions will be sent.";
        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [HttpGet("forgot-password-confirmation")]
    public IActionResult ForgotPasswordConfirmation()
    {
        return View();
    }

    [HttpGet("reset-password")]
    public IActionResult ResetPassword([FromQuery] string? email, [FromQuery] string? token)
    {
        return View(new ResetPasswordPageModel
        {
            Email = email ?? string.Empty,
            Token = token ?? string.Empty
        });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword(ResetPasswordPageModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await userManager.FindByEmailAsync(model.Email.Trim());
        if (user is not { IsActive: true })
        {
            ModelState.AddModelError(string.Empty, "The password reset request is invalid or expired.");
            return View(model);
        }

        var result = await userManager.ResetPasswordAsync(user, model.Token, model.NewPassword);
        if (!result.Succeeded)
        {
            AddIdentityErrors(result);
            return View(model);
        }

        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        TempData["StatusMessage"] = "Password reset. You can sign in with your new password.";
        return RedirectToAction(nameof(Login));
    }

    [HttpGet("verify-email")]
    public async Task<IActionResult> VerifyEmail([FromQuery] string? email, [FromQuery] string? token)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
        {
            ViewData["Verified"] = false;
            ViewData["Message"] = "The email verification link is invalid or expired.";
            return View();
        }

        var user = await userManager.FindByEmailAsync(email.Trim());
        if (user is not { IsActive: true })
        {
            ViewData["Verified"] = false;
            ViewData["Message"] = "The email verification link is invalid or expired.";
            return View();
        }

        var result = await userManager.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
        {
            ViewData["Verified"] = false;
            ViewData["Message"] = "The email verification link is invalid or expired.";
            return View();
        }

        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        ViewData["Verified"] = true;
        ViewData["Message"] = "Your email has been verified.";
        return View();
    }

    [HttpGet("external-login")]
    public IActionResult ExternalLogin([FromQuery] string provider, [FromQuery] string? returnUrl = null)
    {
        var normalizedProvider = TryOutSpotSocialLoginProviders.Normalize(provider);
        if (normalizedProvider is null)
        {
            TempData["StatusMessage"] = "That social login provider is not supported.";
            return RedirectToAction(nameof(Login));
        }

        if (normalizedProvider == TryOutSpotSocialLoginProviders.Google && !googleAuthentication.IsConfigured)
        {
            TempData["StatusMessage"] = "Google login is not configured yet.";
            return RedirectToAction(nameof(Login));
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(ExternalCallback), new
            {
                provider = normalizedProvider,
                returnUrl
            })
        };
        properties.Items["LoginProvider"] = normalizedProvider;

        return Challenge(properties, normalizedProvider);
    }

    [HttpGet("external-callback")]
    public async Task<IActionResult> ExternalCallback(
        [FromQuery] string? provider,
        [FromQuery] string? returnUrl,
        CancellationToken cancellationToken)
    {
        var authenticateResult = await HttpContext.AuthenticateAsync(IdentityConstants.ExternalScheme);
        if (!authenticateResult.Succeeded || authenticateResult.Principal is null)
        {
            TempData["StatusMessage"] = "The social login could not be authenticated.";
            return RedirectToAction(nameof(Login));
        }

        var normalizedProvider = TryOutSpotSocialLoginProviders.Normalize(
            authenticateResult.Properties?.Items.TryGetValue("LoginProvider", out var loginProvider) == true
                ? loginProvider
                : provider);

        if (normalizedProvider is null)
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            TempData["StatusMessage"] = "The social login provider is invalid.";
            return RedirectToAction(nameof(Login));
        }

        var principal = authenticateResult.Principal;
        var providerKey = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(providerKey) || string.IsNullOrWhiteSpace(email))
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            TempData["StatusMessage"] = "The social login provider did not return the required account information.";
            return RedirectToAction(nameof(Login));
        }

        var linkedUser = await userManager.FindByLoginAsync(normalizedProvider, providerKey);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        if (linkedUser is not null)
        {
            if (!linkedUser.IsActive)
            {
                TempData["StatusMessage"] = "The linked account is inactive.";
                return RedirectToAction(nameof(Login));
            }

            await SignInWebUserAsync(linkedUser, isPersistent: true);
            return RedirectToLocalOrOnboarding(returnUrl);
        }

        var ticket = CreateTicket(normalizedProvider, providerKey, email, principal);
        var externalLoginToken = externalLoginTicketService.Create(ticket);

        return RedirectToAction(nameof(CompleteSocialRegistration), new
        {
            externalLoginToken,
            returnUrl
        });
    }

    [HttpGet("complete-social-registration")]
    public async Task<IActionResult> CompleteSocialRegistration(
        [FromQuery] string externalLoginToken,
        [FromQuery] string? returnUrl,
        CancellationToken cancellationToken)
    {
        if (!externalLoginTicketService.TryRead(externalLoginToken, out var ticket))
        {
            TempData["StatusMessage"] = "The social login session expired. Please try again.";
            return RedirectToAction(nameof(Login));
        }

        return View(await PrepareSocialRegistrationModelAsync(new SocialRegistrationPageModel
        {
            ExternalLoginToken = externalLoginToken,
            Provider = ticket.Provider,
            Email = ticket.Email,
            EmailVerified = ticket.EmailVerified,
            FirstName = ticket.FirstName ?? string.Empty,
            LastName = ticket.LastName ?? string.Empty,
            ProfileImageUrl = ticket.ProfileImageUrl,
            AccountTypes = [TryOutSpotRoles.Parent],
            ReturnUrl = returnUrl
        }, cancellationToken));
    }

    [HttpPost("complete-social-registration")]
    public async Task<IActionResult> CompleteSocialRegistration(
        SocialRegistrationPageModel model,
        CancellationToken cancellationToken)
    {
        if (!externalLoginTicketService.TryRead(model.ExternalLoginToken, out var ticket))
        {
            ModelState.AddModelError(string.Empty, "The social login session expired. Please try again.");
            return View(await PrepareSocialRegistrationModelAsync(model, cancellationToken));
        }

        model.Provider = ticket.Provider;
        model.Email = ticket.Email;
        model.EmailVerified = ticket.EmailVerified;
        model.ProfileImageUrl = ticket.ProfileImageUrl;

        var accountTypes = GetValidatedPublicAccountTypes(model.AccountTypes, nameof(model.AccountTypes));
        if (accountTypes.Count == 0)
        {
            ModelState.AddModelError(nameof(model.AccountTypes), "Choose at least one account type.");
        }
        ValidateSmsConsent(model.SmsConsentAccepted, model.PhoneNumber, nameof(model.PhoneNumber));

        if (await userManager.FindByLoginAsync(ticket.Provider, ticket.ProviderKey) is not null)
        {
            ModelState.AddModelError(string.Empty, "This social login is already linked to an account.");
        }

        if (await userManager.FindByEmailAsync(ticket.Email) is not null)
        {
            ModelState.AddModelError(string.Empty, "An account with this email already exists. Sign in and link this social login instead.");
        }

        if (!ModelState.IsValid)
        {
            return View(await PrepareSocialRegistrationModelAsync(model, cancellationToken));
        }

        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            UserName = ticket.Email.Trim(),
            Email = ticket.Email.Trim(),
            EmailConfirmed = ticket.EmailVerified,
            FirstName = NormalizeRequiredName(model.FirstName, ticket.FirstName, "Social"),
            LastName = NormalizeRequiredName(model.LastName, ticket.LastName, "User"),
            PhoneNumber = NormalizeOptional(model.PhoneNumber),
            DateOfBirth = model.DateOfBirth,
            ZipCode = NormalizeOptional(model.ZipCode),
            City = NormalizeOptional(model.City),
            State = NormalizeState(model.State),
            ProfileImageUrl = NormalizeOptional(ticket.ProfileImageUrl),
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true,
            LockoutEnabled = true
        };
        ApplySmsConsent(user, model.SmsConsentAccepted, now, TryOutSpotSmsConsent.SocialRegistrationSource);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var createResult = await userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            AddIdentityErrors(createResult);
            return View(await PrepareSocialRegistrationModelAsync(model, cancellationToken));
        }

        var roleResult = await userManager.AddToRolesAsync(user, accountTypes);
        if (!roleResult.Succeeded)
        {
            AddIdentityErrors(roleResult);
            return View(await PrepareSocialRegistrationModelAsync(model, cancellationToken));
        }

        var loginResult = await userManager.AddLoginAsync(
            user,
            new UserLoginInfo(ticket.Provider, ticket.ProviderKey, GetProviderDisplayName(ticket.Provider)));
        if (!loginResult.Succeeded)
        {
            AddIdentityErrors(loginResult);
            return View(await PrepareSocialRegistrationModelAsync(model, cancellationToken));
        }

        await transaction.CommitAsync(cancellationToken);

        if (!user.EmailConfirmed)
        {
            var confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
            await accountEmailSender.SendEmailConfirmationTokenAsync(user, confirmationToken, cancellationToken);
        }

        await SignInWebUserAsync(user, isPersistent: true);
        return RedirectToLocalOrOnboarding(model.ReturnUrl);
    }

    [HttpPost("link-social-login")]
    public async Task<IActionResult> LinkSocialLogin(
        [FromForm] string externalLoginToken,
        CancellationToken cancellationToken)
    {
        var currentUser = await GetCurrentWebUserAsync();
        if (currentUser is null)
        {
            TempData["StatusMessage"] = "Sign in before linking this social login.";
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(CompleteSocialRegistration), new { externalLoginToken })
            });
        }

        if (!externalLoginTicketService.TryRead(externalLoginToken, out var ticket))
        {
            TempData["StatusMessage"] = "The social login session expired. Please try again.";
            return RedirectToAction(nameof(Login));
        }

        if (!string.Equals(currentUser.Email, ticket.Email, StringComparison.OrdinalIgnoreCase))
        {
            TempData["StatusMessage"] = "Sign in with the matching email account before linking this social login.";
            return RedirectToAction(nameof(CompleteSocialRegistration), new { externalLoginToken });
        }

        var existingLogin = await userManager.FindByLoginAsync(ticket.Provider, ticket.ProviderKey);
        if (existingLogin is not null && existingLogin.Id != currentUser.Id)
        {
            TempData["StatusMessage"] = "This social login is already linked to another account.";
            return RedirectToAction(nameof(Onboarding));
        }

        if (existingLogin is null)
        {
            var result = await userManager.AddLoginAsync(
                currentUser,
                new UserLoginInfo(ticket.Provider, ticket.ProviderKey, GetProviderDisplayName(ticket.Provider)));
            if (!result.Succeeded)
            {
                TempData["StatusMessage"] = string.Join(" ", result.Errors.Select(error => error.Description));
                return RedirectToAction(nameof(CompleteSocialRegistration), new { externalLoginToken });
            }

            currentUser.UpdatedAt = DateTime.UtcNow;
            await userManager.UpdateAsync(currentUser);
            await SignInWebUserAsync(currentUser, isPersistent: true);
        }

        TempData["StatusMessage"] = "Social login linked.";
        return RedirectToAction(nameof(Onboarding));
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpGet("onboarding")]
    public async Task<IActionResult> Onboarding(CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(Onboarding)) });
        }

        return View(await BuildOnboardingPageModelAsync(user, cancellationToken));
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpPost("onboarding/account-types")]
    public async Task<IActionResult> UpdateOnboardingAccountTypes(
        [FromForm] List<string> accountTypes,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(Onboarding)) });
        }

        var validatedAccountTypes = GetValidatedPublicAccountTypes(accountTypes, nameof(accountTypes));
        if (validatedAccountTypes.Count == 0)
        {
            TempData["StatusMessage"] = "Choose at least one account type.";
            return RedirectToAction(nameof(Onboarding));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var currentRoles = await userManager.GetRolesAsync(user);
        var publicRolesToRemove = currentRoles
            .Where(role => TryOutSpotRoles.PublicRegistrationRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (publicRolesToRemove.Length > 0)
        {
            var removeResult = await userManager.RemoveFromRolesAsync(user, publicRolesToRemove);
            if (!removeResult.Succeeded)
            {
                TempData["StatusMessage"] = string.Join(" ", removeResult.Errors.Select(error => error.Description));
                return RedirectToAction(nameof(Onboarding));
            }
        }

        var addResult = await userManager.AddToRolesAsync(user, validatedAccountTypes);
        if (!addResult.Succeeded)
        {
            TempData["StatusMessage"] = string.Join(" ", addResult.Errors.Select(error => error.Description));
            return RedirectToAction(nameof(Onboarding));
        }

        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);
        await transaction.CommitAsync(cancellationToken);
        await SignInWebUserAsync(user, isPersistent: true);

        TempData["StatusMessage"] = "Account types updated.";
        return RedirectToAction(nameof(Onboarding));
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpGet("settings")]
    public async Task<IActionResult> Settings(CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(Settings)) });
        }

        return View(await BuildAccountSettingsPageModelAsync(user, cancellationToken));
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpPost("settings/profile")]
    public async Task<IActionResult> UpdateProfile(
        [Bind(Prefix = "Profile")] ProfileSettingsPageModel model)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(Settings)) });
        }

        if (!ModelState.IsValid)
        {
            TempData["StatusMessage"] = "Review the profile fields and try again.";
            return RedirectToAction(nameof(Settings));
        }

        user.FirstName = model.FirstName.Trim();
        user.LastName = model.LastName.Trim();
        user.DateOfBirth = model.DateOfBirth;
        user.ZipCode = NormalizeOptional(model.ZipCode);
        user.City = NormalizeOptional(model.City);
        user.State = NormalizeState(model.State);
        user.UpdatedAt = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(user);
        TempData["StatusMessage"] = result.Succeeded
            ? "Profile updated."
            : string.Join(" ", result.Errors.Select(error => error.Description));

        return RedirectToAction(nameof(Settings));
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpPost("settings/email")]
    public async Task<IActionResult> RequestEmailChange(
        [Bind(Prefix = "Email")] EmailSettingsPageModel model,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(Settings)) });
        }

        var hasPassword = await userManager.HasPasswordAsync(user);
        if (hasPassword && string.IsNullOrWhiteSpace(model.CurrentPassword))
        {
            ModelState.AddModelError(nameof(model.CurrentPassword), "Enter your current password.");
        }

        if (!ModelState.IsValid)
        {
            TempData["StatusMessage"] = "Review the email change fields and try again.";
            return RedirectToAction(nameof(Settings));
        }

        var newEmail = model.NewEmail.Trim();
        if (string.Equals(user.Email, newEmail, StringComparison.OrdinalIgnoreCase))
        {
            TempData["StatusMessage"] = "That email address is already on your account.";
            return RedirectToAction(nameof(Settings));
        }

        if (hasPassword && !await userManager.CheckPasswordAsync(user, model.CurrentPassword!))
        {
            TempData["StatusMessage"] = "The current password was not correct.";
            return RedirectToAction(nameof(Settings));
        }

        if (await userManager.FindByEmailAsync(newEmail) is not null)
        {
            TempData["StatusMessage"] = "That email address is already used by another TryOutSpot account.";
            return RedirectToAction(nameof(Settings));
        }

        var token = await userManager.GenerateChangeEmailTokenAsync(user, newEmail);
        await accountEmailSender.SendEmailChangeTokenAsync(user, newEmail, token, cancellationToken);

        TempData["StatusMessage"] = "Check the new email address for a confirmation link.";
        return RedirectToAction(nameof(Settings));
    }

    [HttpGet("confirm-email-change")]
    public async Task<IActionResult> ConfirmEmailChange(
        [FromQuery] string? userId,
        [FromQuery] string? email,
        [FromQuery] string? token)
    {
        var webCookieAuthentication = await HttpContext.AuthenticateAsync(TryOutSpotAuthenticationSchemes.WebCookie);
        var signedInUserIdClaim = webCookieAuthentication.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var signedInUserId = Guid.TryParse(signedInUserIdClaim, out var currentUserId)
            ? currentUserId
            : (Guid?)null;

        if (!Guid.TryParse(userId, out var parsedUserId)
            || string.IsNullOrWhiteSpace(email)
            || string.IsNullOrWhiteSpace(token))
        {
            TempData["StatusMessage"] = "The email change link is invalid or expired.";
            return RedirectToAction(nameof(Login));
        }

        var user = await userManager.FindByIdAsync(parsedUserId.ToString());
        if (user is not { IsActive: true })
        {
            TempData["StatusMessage"] = "The email change link is invalid or expired.";
            return RedirectToAction(nameof(Login));
        }

        var result = await userManager.ChangeEmailAsync(user, email.Trim(), token);
        if (!result.Succeeded)
        {
            TempData["StatusMessage"] = "The email change link is invalid or expired.";
            return RedirectToAction(nameof(Login));
        }

        var userNameResult = await userManager.SetUserNameAsync(user, email.Trim());
        if (!userNameResult.Succeeded)
        {
            TempData["StatusMessage"] = string.Join(" ", userNameResult.Errors.Select(error => error.Description));
            return RedirectToAction(nameof(Login));
        }

        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        if (signedInUserId == user.Id)
        {
            await SignInWebUserAsync(user, isPersistent: true);
            TempData["StatusMessage"] = "Email address updated.";
            return RedirectToAction(nameof(Settings));
        }

        TempData["StatusMessage"] = "Email address updated. Sign in with the new address.";
        return RedirectToAction(nameof(Login));
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpPost("settings/phone")]
    public async Task<IActionResult> UpdatePhone(
        [Bind(Prefix = "Phone")] PhoneSettingsPageModel model)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(Settings)) });
        }

        if (!ModelState.IsValid)
        {
            TempData["StatusMessage"] = "Review the phone number and try again.";
            return RedirectToAction(nameof(Settings));
        }

        var phoneNumber = NormalizeOptional(model.PhoneNumber);
        var phoneChanged = !string.Equals(user.PhoneNumber, phoneNumber, StringComparison.Ordinal);
        user.PhoneNumber = phoneNumber;
        if (phoneChanged)
        {
            user.PhoneNumberConfirmed = false;
        }

        user.UpdatedAt = DateTime.UtcNow;
        var result = await userManager.UpdateAsync(user);
        TempData["StatusMessage"] = result.Succeeded
            ? "Phone number updated."
            : string.Join(" ", result.Errors.Select(error => error.Description));

        return RedirectToAction(nameof(Settings));
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpPost("settings/send-phone-code")]
    public async Task<IActionResult> SendPhoneVerification(
        [Bind(Prefix = "Phone")] PhoneSettingsPageModel model,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(Settings)) });
        }

        var phoneNumber = NormalizeOptional(model.PhoneNumber) ?? user.PhoneNumber;
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            TempData["StatusMessage"] = "Add a phone number before sending a verification code.";
            return RedirectToAction(nameof(Settings));
        }

        if (!user.SmsConsentAccepted)
        {
            TempData["StatusMessage"] = "SMS consent is required before sending phone verification messages.";
            return RedirectToAction(nameof(Settings));
        }

        if (!string.Equals(user.PhoneNumber, phoneNumber, StringComparison.Ordinal))
        {
            user.PhoneNumber = phoneNumber;
            user.PhoneNumberConfirmed = false;
            user.UpdatedAt = DateTime.UtcNow;
            await userManager.UpdateAsync(user);
        }

        var verificationCode = await userManager.GenerateChangePhoneNumberTokenAsync(user, phoneNumber);
        await accountSmsSender.SendPhoneVerificationCodeAsync(user, phoneNumber, verificationCode, cancellationToken);

        TempData["StatusMessage"] = "Phone verification code sent.";
        return RedirectToAction(nameof(Settings));
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpPost("settings/verify-phone")]
    public async Task<IActionResult> VerifyPhone(
        [Bind(Prefix = "Phone")] PhoneSettingsPageModel model)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(Settings)) });
        }

        var phoneNumber = NormalizeOptional(model.PhoneNumber) ?? user.PhoneNumber;
        if (string.IsNullOrWhiteSpace(phoneNumber) || string.IsNullOrWhiteSpace(model.VerificationCode))
        {
            TempData["StatusMessage"] = "Enter the phone number and verification code.";
            return RedirectToAction(nameof(Settings));
        }

        var result = await userManager.ChangePhoneNumberAsync(user, phoneNumber, model.VerificationCode.Trim());
        if (!result.Succeeded)
        {
            TempData["StatusMessage"] = string.Join(" ", result.Errors.Select(error => error.Description));
            return RedirectToAction(nameof(Settings));
        }

        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);
        await SignInWebUserAsync(user, isPersistent: true);

        TempData["StatusMessage"] = "Phone number verified.";
        return RedirectToAction(nameof(Settings));
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpPost("settings/password")]
    public async Task<IActionResult> UpdatePassword(
        [Bind(Prefix = "Password")] PasswordSettingsPageModel model)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(Settings)) });
        }

        var hasPassword = await userManager.HasPasswordAsync(user);
        if (hasPassword && string.IsNullOrWhiteSpace(model.CurrentPassword))
        {
            ModelState.AddModelError(nameof(model.CurrentPassword), "Enter your current password.");
        }

        if (!ModelState.IsValid)
        {
            TempData["StatusMessage"] = "Review the password fields and try again.";
            return RedirectToAction(nameof(Settings));
        }

        var result = hasPassword
            ? await userManager.ChangePasswordAsync(user, model.CurrentPassword!, model.NewPassword)
            : await userManager.AddPasswordAsync(user, model.NewPassword);

        if (!result.Succeeded)
        {
            TempData["StatusMessage"] = string.Join(" ", result.Errors.Select(error => error.Description));
            return RedirectToAction(nameof(Settings));
        }

        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);
        await SignInWebUserAsync(user, isPersistent: true);

        TempData["StatusMessage"] = hasPassword ? "Password updated." : "Password added to your account.";
        return RedirectToAction(nameof(Settings));
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpPost("settings/account-types")]
    public async Task<IActionResult> UpdateAccountTypes(
        [FromForm] List<string> accountTypes,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(Settings)) });
        }

        var validatedAccountTypes = GetValidatedPublicAccountTypes(accountTypes, nameof(accountTypes));
        if (validatedAccountTypes.Count == 0)
        {
            TempData["StatusMessage"] = "Choose at least one account type.";
            return RedirectToAction(nameof(Settings));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var currentRoles = await userManager.GetRolesAsync(user);
        var publicRolesToRemove = currentRoles
            .Where(role => TryOutSpotRoles.PublicRegistrationRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (publicRolesToRemove.Length > 0)
        {
            var removeResult = await userManager.RemoveFromRolesAsync(user, publicRolesToRemove);
            if (!removeResult.Succeeded)
            {
                TempData["StatusMessage"] = string.Join(" ", removeResult.Errors.Select(error => error.Description));
                return RedirectToAction(nameof(Settings));
            }
        }

        var addResult = await userManager.AddToRolesAsync(user, validatedAccountTypes);
        if (!addResult.Succeeded)
        {
            TempData["StatusMessage"] = string.Join(" ", addResult.Errors.Select(error => error.Description));
            return RedirectToAction(nameof(Settings));
        }

        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);
        await transaction.CommitAsync(cancellationToken);
        await SignInWebUserAsync(user, isPersistent: true);

        TempData["StatusMessage"] = "Account types updated.";
        return RedirectToAction(nameof(Settings));
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpPost("settings/sms-consent")]
    public async Task<IActionResult> UpdateSmsConsent(
        [Bind(Prefix = "SmsConsent")] SmsConsentSettingsPageModel model)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(Settings)) });
        }

        var phoneNumber = NormalizeOptional(model.PhoneNumber) ?? user.PhoneNumber;
        if (model.SmsConsentAccepted && string.IsNullOrWhiteSpace(phoneNumber))
        {
            TempData["StatusMessage"] = "Add a phone number before opting in to SMS.";
            return RedirectToAction(nameof(Settings));
        }

        if (!ModelState.IsValid)
        {
            TempData["StatusMessage"] = "Review the SMS consent fields and try again.";
            return RedirectToAction(nameof(Settings));
        }

        if (!string.IsNullOrWhiteSpace(phoneNumber)
            && !string.Equals(user.PhoneNumber, phoneNumber, StringComparison.Ordinal))
        {
            user.PhoneNumber = phoneNumber;
            user.PhoneNumberConfirmed = false;
        }

        if (model.SmsConsentAccepted)
        {
            ApplySmsConsent(user, true, DateTime.UtcNow, TryOutSpotSmsConsent.AccountSettingsSource);
        }
        else
        {
            user.SmsConsentAccepted = false;
            user.SmsConsentAcceptedAt = null;
            user.SmsConsentText = null;
            user.SmsConsentSource = TryOutSpotSmsConsent.AccountSettingsOptOutSource;
        }

        user.UpdatedAt = DateTime.UtcNow;
        var result = await userManager.UpdateAsync(user);
        TempData["StatusMessage"] = result.Succeeded
            ? "SMS consent updated."
            : string.Join(" ", result.Errors.Select(error => error.Description));

        return RedirectToAction(nameof(Settings));
    }

    private RegisterPageModel PrepareRegisterModel(RegisterPageModel model)
    {
        model.AvailableAccountTypes = GetAccountTypeOptions(model.AccountTypes);
        return model;
    }

    private async Task<SocialRegistrationPageModel> PrepareSocialRegistrationModelAsync(
        SocialRegistrationPageModel model,
        CancellationToken cancellationToken)
    {
        model.AvailableAccountTypes = GetAccountTypeOptions(model.AccountTypes);
        model.ExistingEmailAccountFound = await userManager.FindByEmailAsync(model.Email) is not null;
        var currentUser = await GetCurrentWebUserAsync();
        model.CanLinkToSignedInAccount = currentUser is not null
            && string.Equals(currentUser.Email, model.Email, StringComparison.OrdinalIgnoreCase);

        return model;
    }

    private async Task<OnboardingPageModel> BuildOnboardingPageModelAsync(
        User user,
        CancellationToken cancellationToken)
    {
        var roles = (await userManager.GetRolesAsync(user))
            .Where(role => TryOutSpotRoles.PublicRegistrationRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            .OrderBy(role => role)
            .ToArray();
        var entitlements = await entitlementService.GetEntitlementsAsync(user.Id, cancellationToken);
        var hasPlayerOrParentRole = HasAnyRole(roles, TryOutSpotRoles.Parent, TryOutSpotRoles.Player);
        var hasTeamOrOrganizationRole = HasAnyRole(
            roles,
            TryOutSpotRoles.Coach,
            TryOutSpotRoles.TeamManager,
            TryOutSpotRoles.AcademyDirector,
            TryOutSpotRoles.OrganizationAdmin);

        var hasLinkedPlayers = hasPlayerOrParentRole
            && await dbContext.UserPlayerRelationships
                .AsNoTracking()
                .AnyAsync(relationship => relationship.UserId == user.Id, cancellationToken);
        var hasTeamRole = hasTeamOrOrganizationRole
            && await dbContext.UserTeamRoles
                .AsNoTracking()
                .AnyAsync(teamRole => teamRole.UserId == user.Id, cancellationToken);
        var subscription = await dbContext.Subscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(currentSubscription => currentSubscription.UserId == user.Id, cancellationToken);
        var subscriptionPlan = TryOutSpotBillingCatalog.GetPlan(subscription?.PlanType);
        var hasPaidPlan = subscriptionPlan?.RequiresStripeSubscription == true
            && TryOutSpotBillingCatalog.IsEntitlingSubscriptionStatus(subscription?.Status);

        var steps = new List<OnboardingStepPageItem>
        {
            new(
                "choose_account_types",
                "Choose account type",
                "Select how you plan to use TryOutSpot.",
                true,
                roles.Length > 0),
            new(
                "verify_email",
                "Verify email",
                "Email verification protects account recovery and sensitive account changes.",
                true,
                user.EmailConfirmed)
        };

        if (hasPlayerOrParentRole)
        {
            steps.Add(new OnboardingStepPageItem(
                "add_player_profile",
                "Add player profile",
                "Create a player profile before registering for tryouts.",
                false,
                hasLinkedPlayers));
        }

        if (hasTeamOrOrganizationRole)
        {
            steps.Add(new OnboardingStepPageItem(
                "add_team_or_organization",
                "Add team or organization",
                "Create or join a team or organization before posting opportunities.",
                false,
                hasTeamRole));
        }

        var recommendedPlans = GetPlansForAccountTypes(roles);
        if (recommendedPlans.Any(plan => plan.RequiresStripeSubscription))
        {
            steps.Add(new OnboardingStepPageItem(
                "choose_plan",
                "Choose plan",
                "Start free and upgrade when premium tools are needed.",
                false,
                hasPaidPlan));
        }

        return new OnboardingPageModel
        {
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            EmailConfirmed = user.EmailConfirmed,
            PhoneNumber = user.PhoneNumber,
            PhoneNumberConfirmed = user.PhoneNumberConfirmed,
            AccountTypes = roles.ToList(),
            AvailableAccountTypes = GetAccountTypeOptions(roles),
            RecommendedPlans = recommendedPlans,
            FeatureCodes = entitlements?.FeatureCodes ?? [],
            Steps = steps
        };
    }

    private async Task<AccountSettingsPageModel> BuildAccountSettingsPageModelAsync(
        User user,
        CancellationToken cancellationToken)
    {
        var roles = (await userManager.GetRolesAsync(user))
            .Where(role => TryOutSpotRoles.PublicRegistrationRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            .OrderBy(role => role)
            .ToArray();
        var entitlements = await entitlementService.GetEntitlementsAsync(user.Id, cancellationToken);
        var subscription = await dbContext.Subscriptions
            .AsNoTracking()
            .SingleOrDefaultAsync(currentSubscription => currentSubscription.UserId == user.Id, cancellationToken);
        var subscriptionPlan = TryOutSpotBillingCatalog.GetPlan(subscription?.PlanType);
        var hasLocalPassword = await userManager.HasPasswordAsync(user);

        return new AccountSettingsPageModel
        {
            Profile = new ProfileSettingsPageModel
            {
                FirstName = user.FirstName,
                LastName = user.LastName,
                DateOfBirth = user.DateOfBirth,
                ZipCode = user.ZipCode,
                City = user.City,
                State = user.State
            },
            Email = new EmailSettingsPageModel
            {
                CurrentEmail = user.Email ?? string.Empty,
                EmailConfirmed = user.EmailConfirmed,
                HasLocalPassword = hasLocalPassword
            },
            Phone = new PhoneSettingsPageModel
            {
                PhoneNumber = user.PhoneNumber,
                PhoneNumberConfirmed = user.PhoneNumberConfirmed
            },
            Password = new PasswordSettingsPageModel
            {
                HasLocalPassword = hasLocalPassword
            },
            AccountTypes = new AccountTypeSettingsPageModel
            {
                AccountTypes = roles.ToList(),
                AvailableAccountTypes = GetAccountTypeOptions(roles)
            },
            SmsConsent = new SmsConsentSettingsPageModel
            {
                SmsConsentAccepted = user.SmsConsentAccepted,
                SmsConsentAcceptedAt = user.SmsConsentAcceptedAt,
                PhoneNumber = user.PhoneNumber
            },
            CurrentPlanName = subscriptionPlan?.Name ?? "Free Player/Parent",
            CurrentPlanStatus = subscription?.Status,
            RecommendedPlans = GetPlansForAccountTypes(roles),
            FeatureCodes = entitlements?.FeatureCodes ?? []
        };
    }

    private async Task SignInWebUserAsync(User user, bool isPersistent)
    {
        var roles = await userManager.GetRolesAsync(user);
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Email ?? user.UserName ?? user.Id.ToString()),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new("security_stamp", user.SecurityStamp ?? string.Empty)
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, TryOutSpotAuthenticationSchemes.WebCookie);
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(
            TryOutSpotAuthenticationSchemes.WebCookie,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = isPersistent,
                ExpiresUtc = isPersistent ? DateTimeOffset.UtcNow.AddDays(14) : null
            });
    }

    private async Task<User?> GetCurrentWebUserAsync()
    {
        var result = await HttpContext.AuthenticateAsync(TryOutSpotAuthenticationSchemes.WebCookie);
        var userIdClaim = result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return null;
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        var tokenSecurityStamp = result.Principal?.FindFirstValue("security_stamp");
        if (user is not { IsActive: true }
            || !string.Equals(user.SecurityStamp, tokenSecurityStamp, StringComparison.Ordinal))
        {
            await HttpContext.SignOutAsync(TryOutSpotAuthenticationSchemes.WebCookie);
            return null;
        }

        return user;
    }

    private IActionResult RedirectToLocalOrOnboarding(string? returnUrl)
    {
        return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl)
            : RedirectToAction(nameof(Onboarding));
    }

    private static IReadOnlyCollection<AccountTypeSelectionItem> GetAccountTypeOptions(
        IEnumerable<string> selectedAccountTypes)
    {
        var selected = selectedAccountTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return
        [
            new(
                TryOutSpotRoles.Parent,
                "Parent or guardian",
                "Find tryouts, manage player profiles, and track registrations.",
                true,
                selected.Contains(TryOutSpotRoles.Parent)),
            new(
                TryOutSpotRoles.Player,
                "Player",
                "Build a profile and discover baseball or softball opportunities.",
                true,
                selected.Contains(TryOutSpotRoles.Player)),
            new(
                TryOutSpotRoles.Coach,
                "Coach",
                "Find players, manage tryouts, and communicate with families.",
                true,
                selected.Contains(TryOutSpotRoles.Coach)),
            new(
                TryOutSpotRoles.TeamManager,
                "Team manager",
                "Help operate a team, registrations, and opportunity listings.",
                false,
                selected.Contains(TryOutSpotRoles.TeamManager)),
            new(
                TryOutSpotRoles.AcademyDirector,
                "Academy director",
                "Manage academy-level teams, listings, and development programs.",
                false,
                selected.Contains(TryOutSpotRoles.AcademyDirector)),
            new(
                TryOutSpotRoles.OrganizationAdmin,
                "Organization admin",
                "Manage multi-team organizations, staff, and workflows.",
                false,
                selected.Contains(TryOutSpotRoles.OrganizationAdmin))
        ];
    }

    private static IReadOnlyCollection<BillingPlanResponse> GetPlansForAccountTypes(IEnumerable<string> accountTypes)
    {
        var roles = accountTypes
            .Select(TryOutSpotRoles.NormalizePublicRegistrationRole)
            .Where(role => role is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var planCodes = new List<string>();
        if (roles.Count == 0 || roles.Contains(TryOutSpotRoles.Parent) || roles.Contains(TryOutSpotRoles.Player))
        {
            planCodes.Add(TryOutSpotPlanCodes.FreePlayerParent);
            planCodes.Add(TryOutSpotPlanCodes.PremiumPlayer);
        }

        if (roles.Contains(TryOutSpotRoles.Coach)
            || roles.Contains(TryOutSpotRoles.TeamManager)
            || roles.Contains(TryOutSpotRoles.AcademyDirector))
        {
            planCodes.Add(TryOutSpotPlanCodes.TeamBasic);
            planCodes.Add(TryOutSpotPlanCodes.TeamProfessional);
        }

        if (roles.Contains(TryOutSpotRoles.OrganizationAdmin)
            || roles.Contains(TryOutSpotRoles.AcademyDirector))
        {
            planCodes.Add(TryOutSpotPlanCodes.EnterpriseOrganization);
        }

        return planCodes
            .Distinct(StringComparer.Ordinal)
            .Select(TryOutSpotBillingCatalog.GetPlan)
            .Where(plan => plan is not null)
            .Cast<BillingPlanDefinition>()
            .Select(plan => new BillingPlanResponse(
                plan.Code,
                plan.Name,
                plan.Audience,
                plan.Description,
                plan.MonthlyAmount,
                plan.AnnualAmount,
                plan.Currency,
                plan.TrialDays,
                plan.RequiresStripeSubscription,
                plan.IncludedFeatureCodes))
            .ToArray();
    }

    private List<string> GetValidatedPublicAccountTypes(
        IReadOnlyCollection<string>? requestedAccountTypes,
        string modelStateKey)
    {
        if (requestedAccountTypes is null || requestedAccountTypes.Count == 0)
        {
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

    private static ExternalLoginTicket CreateTicket(
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

    private static bool IsEmailVerified(string provider, ClaimsPrincipal principal)
    {
        if (provider == TryOutSpotSocialLoginProviders.Google)
        {
            var value = principal.FindFirstValue("urn:google:email_verified");
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        return false;
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

    private static bool HasAnyRole(IReadOnlyCollection<string> roles, params string[] candidates)
    {
        return roles.Any(role => candidates.Contains(role, StringComparer.OrdinalIgnoreCase));
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
