using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
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
    IStripeBillingService stripeBillingService,
    IOptions<GoogleAuthenticationOptions> googleOptions,
    IOptions<StripeBillingOptions> stripeOptions) : Controller
{
    private readonly GoogleAuthenticationOptions googleAuthentication = googleOptions.Value;
    private readonly StripeBillingOptions stripeBillingOptions = stripeOptions.Value;
    private static readonly string[] PlayerRelationshipOptions = ["Parent", "Guardian", "Self", "Coach", "Other"];
    private static readonly string[] PlayerContactVisibilityOptions = ["Public", "VerifiedCoachesOnly"];
    private static readonly string[] TeamOnboardingRoleOptions =
    [
        TryOutSpotRoles.Coach,
        TryOutSpotRoles.TeamManager,
        TryOutSpotRoles.AcademyDirector,
        TryOutSpotRoles.OrganizationAdmin
    ];
    private static readonly string[] TeamGeographicScopeOptions = ["Local", "Regional", "National"];

    [HttpGet("register")]
    public async Task<IActionResult> Register([FromQuery] string? returnUrl = null)
    {
        var model = PrepareRegisterModel(new RegisterPageModel { ReturnUrl = returnUrl });
        await PopulateExternalProviderAvailabilityAsync(model);
        return View(model);
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
        await PopulateExternalProviderAvailabilityAsync(model);

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
    public async Task<IActionResult> Login(
        [FromQuery] string? returnUrl = null,
        [FromQuery] string? externalLoginStatus = null)
    {
        if (string.Equals(externalLoginStatus, "failed", StringComparison.OrdinalIgnoreCase))
        {
            TempData["StatusMessage"] = "The social login session expired or could not be verified. Please start social sign-in again.";
        }

        var model = new LoginPageModel
        {
            ReturnUrl = returnUrl
        };
        await PopulateExternalProviderAvailabilityAsync(model);
        return View(model);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginPageModel model, CancellationToken cancellationToken)
    {
        await PopulateExternalProviderAvailabilityAsync(model);
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

        var ticket = CreateTicket(normalizedProvider, providerKey, email, principal);
        var linkedUser = await userManager.FindByLoginAsync(normalizedProvider, providerKey);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        if (linkedUser is not null)
        {
            if (!linkedUser.IsActive)
            {
                TempData["StatusMessage"] = "The linked account is inactive.";
                return RedirectToAction(nameof(Login));
            }

            if (ApplyVerifiedExternalEmail(linkedUser, ticket, DateTime.UtcNow))
            {
                await userManager.UpdateAsync(linkedUser);
            }

            await SignInWebUserAsync(linkedUser, isPersistent: true);
            return RedirectToLocalOrOnboarding(returnUrl);
        }

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
        [FromForm] string? returnUrl,
        CancellationToken cancellationToken)
    {
        if (!externalLoginTicketService.TryRead(externalLoginToken, out var ticket))
        {
            TempData["StatusMessage"] = "The social login session expired. Please try again.";
            return RedirectToAction(nameof(Login));
        }

        var existingLogin = await userManager.FindByLoginAsync(ticket.Provider, ticket.ProviderKey);
        var currentUser = await GetCurrentWebUserAsync();
        if (existingLogin is not null)
        {
            if (currentUser is not null && existingLogin.Id != currentUser.Id)
            {
                TempData["StatusMessage"] = "This social login is already linked to another account.";
                return RedirectToLocalOrOnboarding(returnUrl);
            }

            if (!existingLogin.IsActive)
            {
                TempData["StatusMessage"] = "The linked account is inactive.";
                return RedirectToAction(nameof(Login));
            }

            if (ApplyVerifiedExternalEmail(existingLogin, ticket, DateTime.UtcNow))
            {
                await userManager.UpdateAsync(existingLogin);
            }

            await SignInWebUserAsync(existingLogin, isPersistent: true);
            TempData["StatusMessage"] = "Signed in with social login.";
            return RedirectToLocalOrOnboarding(returnUrl);
        }

        User? userToLink;
        if (currentUser is null)
        {
            if (!ticket.EmailVerified)
            {
                TempData["StatusMessage"] = "Sign in before linking this social login.";
                return RedirectToAction(nameof(Login), new
                {
                    returnUrl = Url.Action(nameof(CompleteSocialRegistration), new { externalLoginToken, returnUrl })
                });
            }

            userToLink = await userManager.FindByEmailAsync(ticket.Email.Trim());
            if (userToLink is null)
            {
                return RedirectToAction(nameof(CompleteSocialRegistration), new { externalLoginToken, returnUrl });
            }

            if (!userToLink.IsActive)
            {
                TempData["StatusMessage"] = "The matching account is inactive.";
                return RedirectToAction(nameof(Login));
            }
        }
        else
        {
            if (!string.Equals(currentUser.Email, ticket.Email, StringComparison.OrdinalIgnoreCase))
            {
                TempData["StatusMessage"] = "Sign in with the matching email account before linking this social login.";
                return RedirectToAction(nameof(CompleteSocialRegistration), new { externalLoginToken, returnUrl });
            }

            userToLink = currentUser;
        }

        var result = await userManager.AddLoginAsync(
            userToLink,
            new UserLoginInfo(ticket.Provider, ticket.ProviderKey, GetProviderDisplayName(ticket.Provider)));
        if (!result.Succeeded)
        {
            TempData["StatusMessage"] = string.Join(" ", result.Errors.Select(error => error.Description));
            return RedirectToAction(nameof(CompleteSocialRegistration), new { externalLoginToken, returnUrl });
        }

        var now = DateTime.UtcNow;
        userToLink.UpdatedAt = now;
        ApplyVerifiedExternalEmail(userToLink, ticket, now);
        await userManager.UpdateAsync(userToLink);
        await SignInWebUserAsync(userToLink, isPersistent: true);

        TempData["StatusMessage"] = "Social login linked.";
        return RedirectToLocalOrOnboarding(returnUrl);
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
    [HttpGet("onboarding/add-player-profile")]
    public async Task<IActionResult> AddPlayerProfile(CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(AddPlayerProfile)) });
        }

        var model = await BuildAddPlayerProfilePageModelAsync(
            user,
            new AddPlayerProfilePageModel
            {
                ContactEmail = user.Email,
                ContactPhone = user.PhoneNumber,
                City = user.City,
                State = user.State,
                ZipCode = user.ZipCode,
                DateOfBirth = DateTime.UtcNow.Date.AddYears(-12)
            },
            cancellationToken);
        return View(model);
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpPost("onboarding/add-player-profile")]
    public async Task<IActionResult> AddPlayerProfile(
        AddPlayerProfilePageModel model,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(AddPlayerProfile)) });
        }

        var relationship = NormalizeRelationship(model.Relationship);
        if (string.IsNullOrWhiteSpace(relationship))
        {
            ModelState.AddModelError(nameof(model.Relationship), "Choose a relationship.");
        }

        var contactVisibility = NormalizePlayerContactVisibility(model.ContactVisibility);
        if (contactVisibility is null)
        {
            ModelState.AddModelError(nameof(model.ContactVisibility), "Choose whether contact details are public or limited to verified coaches.");
        }

        var selectedSportDetails = model.SportDetails
            .Where(detail => detail.IsSelected)
            .GroupBy(detail => detail.SportId)
            .Select(group => group.First())
            .ToArray();
        var selectedSportIds = selectedSportDetails
            .Select(detail => detail.SportId)
            .ToArray();
        var selectedSports = selectedSportIds.Length == 0
            ? Array.Empty<Guid>()
            : await dbContext.Sports
                .AsNoTracking()
                .Where(sport => sport.IsActive && selectedSportIds.Contains(sport.Id))
                .Select(sport => sport.Id)
                .ToArrayAsync(cancellationToken);
        if (selectedSports.Length != selectedSportIds.Length)
        {
            ModelState.AddModelError(nameof(model.SportDetails), "One or more selected sports are no longer available.");
        }

        for (var sportIndex = 0; sportIndex < model.SportDetails.Count; sportIndex++)
        {
            var sportDetail = model.SportDetails[sportIndex];
            if (!sportDetail.IsSelected)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(sportDetail.SkillLevel))
            {
                ModelState.AddModelError($"SportDetails[{sportIndex}].SkillLevel", "Enter ability level for each selected sport.");
            }
        }

        if (!ModelState.IsValid)
        {
            return View(await BuildAddPlayerProfilePageModelAsync(user, model, cancellationToken));
        }

        var now = DateTime.UtcNow;
        var socialMediaLinks = SerializeLinkCollection(
            ("facebook", model.FacebookPageUrl),
            ("x", NormalizeSocialHandleOrUrl(model.XPageUrl, "https://x.com/")),
            ("instagram", NormalizeSocialHandleOrUrl(model.InstagramUrl, "https://instagram.com/")),
            ("youtube", model.YouTubeUrl),
            ("tiktok", NormalizeSocialHandleOrUrl(model.TikTokUrl, "https://tiktok.com/")),
            ("highlight_video_1", model.HighlightVideoUrl1),
            ("highlight_video_2", model.HighlightVideoUrl2));
        var recruitingProfileLinks = SerializeLinkCollection(
            ("sportsrecruits", model.SportsRecruitsProfileUrl),
            ("fieldlevel", model.FieldLevelProfileUrl),
            ("ncsa", model.NcsaProfileUrl),
            ("other", model.OtherRecruitingProfileUrl));
        var playerId = Guid.NewGuid();
        var player = new Player
        {
            Id = playerId,
            FirstName = model.FirstName.Trim(),
            LastName = model.LastName.Trim(),
            DateOfBirth = NormalizeUtcDate(model.DateOfBirth),
            ContactEmail = NormalizeOptional(model.ContactEmail),
            ContactPhone = NormalizeOptional(model.ContactPhone),
            ProfileImageUrl = NormalizeOptional(model.ProfileImageUrl),
            Height = NormalizeOptional(model.Height),
            Weight = NormalizeOptional(model.Weight),
            ThrowsHand = NormalizeOptional(model.ThrowsHand),
            BatsHand = NormalizeOptional(model.BatsHand),
            SchoolName = NormalizeOptional(model.SchoolName),
            CurrentTeamName = NormalizeOptional(model.CurrentTeamName),
            GraduationYear = model.GraduationYear,
            ContactVisibility = contactVisibility!,
            City = NormalizeOptional(model.City),
            State = NormalizeState(model.State),
            ZipCode = NormalizeOptional(model.ZipCode),
            SocialMediaLinks = socialMediaLinks,
            RecruitingProfileLinks = recruitingProfileLinks,
            IsSearchable = model.IsSearchable,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };

        var relationshipToUser = new UserPlayerRelationship
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            PlayerId = playerId,
            Relationship = relationship!,
            CanManage = model.CanManage,
            CreatedAt = now
        };

        dbContext.Players.Add(player);
        dbContext.UserPlayerRelationships.Add(relationshipToUser);

        var selectedSportSet = selectedSports.ToHashSet();
        foreach (var sportDetail in selectedSportDetails)
        {
            if (!selectedSportSet.Contains(sportDetail.SportId))
            {
                continue;
            }

            dbContext.PlayerSports.Add(new PlayerSport
            {
                Id = Guid.NewGuid(),
                PlayerId = playerId,
                SportId = sportDetail.SportId,
                SkillLevel = NormalizeOptional(sportDetail.SkillLevel),
                PrimaryPosition = NormalizeOptional(sportDetail.PrimaryPosition),
                SecondaryPositions = NormalizeOptional(sportDetail.SecondaryPositions),
                IsActive = true,
                CreatedAt = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = "Player profile added.";
        return RedirectToAction(nameof(Onboarding));
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpGet("onboarding/add-team-or-organization")]
    public async Task<IActionResult> AddTeamOrOrganization(CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(AddTeamOrOrganization)) });
        }

        var model = await BuildAddTeamOrOrganizationPageModelAsync(
            user,
            new AddTeamOrOrganizationPageModel
            {
                ContactEmail = user.Email,
                ContactPhone = user.PhoneNumber,
                City = user.City,
                State = user.State,
                ZipCode = user.ZipCode
            },
            cancellationToken);

        if (model.AvailableTeamRoleOptions.Count == 0)
        {
            TempData["StatusMessage"] = "Select a coach, team manager, academy director, or organization admin account type before adding a team.";
            return RedirectToAction(nameof(Onboarding));
        }

        return View(model);
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpPost("onboarding/add-team-or-organization")]
    public async Task<IActionResult> AddTeamOrOrganization(
        AddTeamOrOrganizationPageModel model,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(AddTeamOrOrganization)) });
        }

        model = await BuildAddTeamOrOrganizationPageModelAsync(user, model, cancellationToken);
        if (model.AvailableTeamRoleOptions.Count == 0)
        {
            TempData["StatusMessage"] = "Select a coach, team manager, academy director, or organization admin account type before adding a team.";
            return RedirectToAction(nameof(Onboarding));
        }

        var createType = NormalizeTeamCreateType(model.CreateType);
        if (createType is null)
        {
            ModelState.AddModelError(nameof(model.CreateType), "Choose whether you are creating a team or an organization.");
        }

        var teamRole = NormalizeTeamOnboardingRole(model.TeamRole, model.AvailableTeamRoleOptions);
        if (teamRole is null)
        {
            ModelState.AddModelError(nameof(model.TeamRole), "Choose a team role that matches your account type.");
        }

        var geographicScope = NormalizeTeamGeographicScope(model.GeographicScope);
        if (geographicScope is null)
        {
            ModelState.AddModelError(nameof(model.GeographicScope), "Choose Local, Regional, or National.");
        }

        if (string.Equals(createType, "organization", StringComparison.Ordinal)
            && string.IsNullOrWhiteSpace(model.OrganizationName))
        {
            ModelState.AddModelError(nameof(model.OrganizationName), "Enter an organization name when creating an organization.");
        }

        var selectedSportIds = model.SelectedSportIds.Distinct().ToArray();
        var selectedSports = selectedSportIds.Length == 0
            ? Array.Empty<Guid>()
            : await dbContext.Sports
                .AsNoTracking()
                .Where(sport => sport.IsActive && selectedSportIds.Contains(sport.Id))
                .Select(sport => sport.Id)
                .ToArrayAsync(cancellationToken);
        if (selectedSports.Length != selectedSportIds.Length)
        {
            ModelState.AddModelError(nameof(model.SelectedSportIds), "One or more selected sports are no longer available.");
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var now = DateTime.UtcNow;
        var socialMediaLinks = SerializeLinkCollection(
            ("facebook", model.FacebookPageUrl),
            ("x", NormalizeSocialHandleOrUrl(model.XPageUrl, "https://x.com/")),
            ("instagram", NormalizeSocialHandleOrUrl(model.InstagramUrl, "https://instagram.com/")),
            ("youtube", model.YouTubeUrl),
            ("tiktok", NormalizeSocialHandleOrUrl(model.TikTokUrl, "https://tiktok.com/")),
            ("highlight_video_1", model.HighlightVideoUrl1),
            ("highlight_video_2", model.HighlightVideoUrl2));
        Organization? organization = null;
        if (string.Equals(createType, "organization", StringComparison.Ordinal))
        {
            organization = new Organization
            {
                Id = Guid.NewGuid(),
                Name = model.OrganizationName!.Trim(),
                LogoImageUrl = NormalizeOptional(model.ProfileImageUrl),
                WebsiteUrl = NormalizeOptional(model.WebsiteUrl),
                City = NormalizeOptional(model.City),
                State = NormalizeState(model.State),
                ZipCode = NormalizeOptional(model.ZipCode),
                PhoneNumber = NormalizeOptional(model.ContactPhone),
                Email = NormalizeOptional(model.ContactEmail),
                SocialMediaLinks = socialMediaLinks,
                IsSearchable = model.IsSearchable,
                IsAcademy = string.Equals(teamRole, TryOutSpotRoles.AcademyDirector, StringComparison.Ordinal),
                IsVerified = false,
                CreatedAt = now,
                UpdatedAt = now,
                IsActive = true
            };
            dbContext.Organizations.Add(organization);
        }

        var teamId = Guid.NewGuid();
        var team = new Team
        {
            Id = teamId,
            OrganizationId = organization?.Id,
            Name = model.TeamName.Trim(),
            TeamLevel = NormalizeOptional(model.TeamLevel),
            GeographicScope = geographicScope!,
            LogoImageUrl = NormalizeOptional(model.ProfileImageUrl),
            WebsiteUrl = NormalizeOptional(model.WebsiteUrl),
            City = NormalizeOptional(model.City),
            State = NormalizeState(model.State),
            ZipCode = NormalizeOptional(model.ZipCode),
            PhoneNumber = NormalizeOptional(model.ContactPhone),
            Email = NormalizeOptional(model.ContactEmail),
            SocialMediaLinks = socialMediaLinks,
            IsSearchable = model.IsSearchable,
            IsElite = false,
            IsVerified = false,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };

        var userTeamRole = new UserTeamRole
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TeamId = teamId,
            Role = teamRole!,
            StartDate = now,
            IsActive = true,
            CreatedAt = now
        };

        dbContext.Teams.Add(team);
        dbContext.UserTeamRoles.Add(userTeamRole);

        foreach (var sportId in selectedSports)
        {
            dbContext.TeamSports.Add(new TeamSport
            {
                Id = Guid.NewGuid(),
                TeamId = teamId,
                SportId = sportId,
                IsActive = true,
                CreatedAt = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = "Team setup saved.";
        return RedirectToAction(nameof(Onboarding));
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpGet("onboarding/choose-plan")]
    public async Task<IActionResult> ChoosePlan(CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(ChoosePlan)) });
        }

        var model = await BuildChoosePlanPageModelAsync(user, new ChoosePlanPageModel(), cancellationToken);
        return View(model);
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpPost("onboarding/choose-plan")]
    public async Task<IActionResult> ChoosePlan(
        ChoosePlanPageModel model,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(ChoosePlan)) });
        }

        var roles = (await userManager.GetRolesAsync(user))
            .Where(role => TryOutSpotRoles.PublicRegistrationRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var availablePlans = GetPlansForAccountTypes(roles);
        if (availablePlans.Count == 0)
        {
            TempData["StatusMessage"] = "No plan options are available for the selected account types yet.";
            return RedirectToAction(nameof(Onboarding));
        }

        var normalizedPlanCode = TryOutSpotBillingCatalog.NormalizePlanCode(model.PlanCode);
        var selectedPlan = availablePlans
            .FirstOrDefault(plan => string.Equals(plan.Code, normalizedPlanCode, StringComparison.Ordinal));
        if (selectedPlan is null)
        {
            ModelState.AddModelError(nameof(model.PlanCode), "Choose one of the available plans.");
            return View(await BuildChoosePlanPageModelAsync(user, model, cancellationToken));
        }

        var billingInterval = BillingIntervalCodes.Normalize(model.BillingInterval);
        if (billingInterval is null)
        {
            ModelState.AddModelError(nameof(model.BillingInterval), "Choose monthly or annual billing.");
            return View(await BuildChoosePlanPageModelAsync(user, model, cancellationToken));
        }

        var amount = billingInterval switch
        {
            BillingIntervalCodes.Month => selectedPlan.MonthlyAmount,
            BillingIntervalCodes.Year => selectedPlan.AnnualAmount,
            _ => null
        };
        if (amount is null)
        {
            ModelState.AddModelError(nameof(model.BillingInterval), "This plan does not offer the selected billing interval.");
            return View(await BuildChoosePlanPageModelAsync(user, model, cancellationToken));
        }

        var now = DateTime.UtcNow;
        var subscription = await FindOrCreateAccountScopeSubscriptionAsync(
            user,
            selectedPlan.Code,
            now,
            cancellationToken);

        subscription.PlanType = selectedPlan.Code;
        subscription.ScopeType = TryOutSpotSubscriptionScopeTypes.Account;
        subscription.ScopeId = null;
        subscription.Currency = selectedPlan.Currency;
        subscription.BillingInterval = billingInterval;
        subscription.Amount = amount;
        subscription.UpdatedAt = now;
        subscription.CancelAtPeriodEnd = false;
        subscription.CancelledAt = null;
        subscription.IsElite = string.Equals(selectedPlan.Code, TryOutSpotPlanCodes.PremiumPlayer, StringComparison.Ordinal);

        if (!selectedPlan.RequiresStripeSubscription)
        {
            subscription.Status = "active";
            subscription.CurrentPeriodStart = now;
            subscription.CurrentPeriodEnd = null;
            subscription.TrialEnd = null;
            await dbContext.SaveChangesAsync(cancellationToken);

            TempData["StatusMessage"] = $"{selectedPlan.Name} selected.";
            return RedirectToAction(nameof(Onboarding));
        }

        var stripePriceId = stripeBillingOptions.GetPriceId(selectedPlan.Code, billingInterval);
        if (!stripeBillingOptions.IsConfigured || string.IsNullOrWhiteSpace(stripePriceId))
        {
            subscription.Status = "plan_selected";
            await dbContext.SaveChangesAsync(cancellationToken);

            TempData["StatusMessage"] = "Plan selected. Checkout will be available as soon as Stripe is fully configured.";
            return RedirectToAction(nameof(Onboarding));
        }

        var planDefinition = TryOutSpotBillingCatalog.GetPlan(selectedPlan.Code);
        if (planDefinition is null)
        {
            ModelState.AddModelError(nameof(model.PlanCode), "The selected plan is no longer available.");
            return View(await BuildChoosePlanPageModelAsync(user, model, cancellationToken));
        }

        var existingStripeCustomerId = await ResolveExistingStripeCustomerIdAsync(user.Id, cancellationToken);
        var checkoutSession = await stripeBillingService.CreateCheckoutSessionAsync(
            user,
            existingStripeCustomerId,
            planDefinition,
            billingInterval,
            stripePriceId,
            TryOutSpotSubscriptionScopeTypes.Account,
            null,
            cancellationToken);

        subscription.Status = "checkout_started";
        subscription.StripeCustomerId = checkoutSession.StripeCustomerId;
        subscription.StripePriceId = stripePriceId;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Redirect(checkoutSession.Url);
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

    private async Task PopulateExternalProviderAvailabilityAsync(RegisterPageModel model)
    {
        var availability = await GetExternalProviderAvailabilityAsync();
        model.GoogleIsConfigured = availability.GoogleIsConfigured;
        model.FacebookIsConfigured = availability.FacebookIsConfigured;
        model.AppleIsConfigured = availability.AppleIsConfigured;
    }

    private async Task PopulateExternalProviderAvailabilityAsync(LoginPageModel model)
    {
        var availability = await GetExternalProviderAvailabilityAsync();
        model.GoogleIsConfigured = availability.GoogleIsConfigured;
        model.FacebookIsConfigured = availability.FacebookIsConfigured;
        model.AppleIsConfigured = availability.AppleIsConfigured;
    }

    private async Task<ExternalProviderAvailability> GetExternalProviderAvailabilityAsync()
    {
        var schemeProvider = HttpContext.RequestServices.GetRequiredService<IAuthenticationSchemeProvider>();
        var configuredProviders = (await schemeProvider.GetAllSchemesAsync())
            .Select(scheme => scheme.Name)
            .Where(schemeName => !string.IsNullOrWhiteSpace(schemeName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new ExternalProviderAvailability(
            configuredProviders.Contains(TryOutSpotSocialLoginProviders.Google),
            configuredProviders.Contains(TryOutSpotSocialLoginProviders.Facebook),
            configuredProviders.Contains(TryOutSpotSocialLoginProviders.Apple));
    }

    private sealed record ExternalProviderAvailability(
        bool GoogleIsConfigured,
        bool FacebookIsConfigured,
        bool AppleIsConfigured);

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
        var subscriptions = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(currentSubscription => currentSubscription.UserId == user.Id)
            .ToArrayAsync(cancellationToken);
        var recommendedPlans = GetPlansForAccountTypes(roles);
        var hasPlanSelection = HasSelectedAnyRecommendedPlan(subscriptions, recommendedPlans);

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

        if (recommendedPlans.Any(plan => plan.RequiresStripeSubscription))
        {
            steps.Add(new OnboardingStepPageItem(
                "choose_plan",
                "Choose plan",
                "Start free and upgrade when premium tools are needed.",
                false,
                hasPlanSelection));
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

    private async Task<AddPlayerProfilePageModel> BuildAddPlayerProfilePageModelAsync(
        User user,
        AddPlayerProfilePageModel model,
        CancellationToken cancellationToken)
    {
        var selectedSportIds = model.SportDetails
            .Where(detail => detail.IsSelected)
            .Select(detail => detail.SportId)
            .ToHashSet();
        var availableSports = await dbContext.Sports
            .AsNoTracking()
            .Where(sport => sport.IsActive)
            .OrderBy(sport => sport.Name)
            .Select(sport => new SportSelectionPageItem(
                sport.Id,
                sport.Name,
                selectedSportIds.Contains(sport.Id)))
            .ToArrayAsync(cancellationToken);

        var sportDetailsById = model.SportDetails
            .Where(detail => detail.SportId != Guid.Empty)
            .ToDictionary(detail => detail.SportId, detail => detail);
        var sportDetails = availableSports.Select(sport =>
        {
            if (sportDetailsById.TryGetValue(sport.Id, out var existingDetail))
            {
                return new PlayerSportDetailPageModel
                {
                    SportId = sport.Id,
                    SportName = sport.Name,
                    IsSelected = existingDetail.IsSelected,
                    SkillLevel = existingDetail.SkillLevel,
                    PrimaryPosition = existingDetail.PrimaryPosition,
                    SecondaryPositions = existingDetail.SecondaryPositions
                };
            }

            return new PlayerSportDetailPageModel
            {
                SportId = sport.Id,
                SportName = sport.Name,
                IsSelected = sport.IsSelected
            };
        }).ToList();

        model.SportDetails = sportDetails;
        model.AvailableSports = availableSports;
        model.AvailableRelationshipOptions = PlayerRelationshipOptions;
        model.AvailableContactVisibilityOptions = PlayerContactVisibilityOptions;
        model.ContactVisibility = NormalizePlayerContactVisibility(model.ContactVisibility)
            ?? PlayerContactVisibilityOptions[1];
        model.ContactEmail = string.IsNullOrWhiteSpace(model.ContactEmail)
            ? user.Email
            : model.ContactEmail;
        model.ContactPhone = string.IsNullOrWhiteSpace(model.ContactPhone)
            ? user.PhoneNumber
            : model.ContactPhone;
        model.City = string.IsNullOrWhiteSpace(model.City)
            ? user.City
            : model.City;
        model.State = string.IsNullOrWhiteSpace(model.State)
            ? user.State
            : model.State;
        model.ZipCode = string.IsNullOrWhiteSpace(model.ZipCode)
            ? user.ZipCode
            : model.ZipCode;

        return model;
    }

    private async Task<ChoosePlanPageModel> BuildChoosePlanPageModelAsync(
        User user,
        ChoosePlanPageModel model,
        CancellationToken cancellationToken)
    {
        var roles = (await userManager.GetRolesAsync(user))
            .Where(role => TryOutSpotRoles.PublicRegistrationRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var availablePlans = GetPlansForAccountTypes(roles);
        var latestSelection = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == user.Id
                && subscription.ScopeType == TryOutSpotSubscriptionScopeTypes.Account)
            .OrderByDescending(subscription => subscription.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        model.AvailablePlans = availablePlans;
        model.StripeIsConfigured = stripeBillingOptions.IsConfigured;

        if (string.IsNullOrWhiteSpace(model.PlanCode))
        {
            var currentPlanCode = TryOutSpotBillingCatalog.NormalizePlanCode(latestSelection?.PlanType);
            var defaultPlanCode = currentPlanCode is not null
                && availablePlans.Any(plan => string.Equals(plan.Code, currentPlanCode, StringComparison.Ordinal))
                ? currentPlanCode
                : availablePlans.FirstOrDefault()?.Code;
            model.PlanCode = defaultPlanCode ?? string.Empty;
        }

        model.BillingInterval = BillingIntervalCodes.Normalize(model.BillingInterval) ?? BillingIntervalCodes.Month;
        var selectedPlan = availablePlans
            .FirstOrDefault(plan => string.Equals(plan.Code, model.PlanCode, StringComparison.Ordinal));
        model.CheckoutAvailableForSelection = selectedPlan is not null
            && selectedPlan.RequiresStripeSubscription
            && stripeBillingOptions.IsConfigured
            && stripeBillingOptions.GetPriceId(selectedPlan.Code, model.BillingInterval) is not null;

        return model;
    }

    private async Task<AddTeamOrOrganizationPageModel> BuildAddTeamOrOrganizationPageModelAsync(
        User user,
        AddTeamOrOrganizationPageModel model,
        CancellationToken cancellationToken)
    {
        var roles = (await userManager.GetRolesAsync(user))
            .Where(role => TryOutSpotRoles.PublicRegistrationRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var availableTeamRoleOptions = TeamOnboardingRoleOptions
            .Where(teamRole => roles.Contains(teamRole, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var selectedSportIds = model.SelectedSportIds.ToHashSet();
        var availableSports = await dbContext.Sports
            .AsNoTracking()
            .Where(sport => sport.IsActive)
            .OrderBy(sport => sport.Name)
            .Select(sport => new SportSelectionPageItem(
                sport.Id,
                sport.Name,
                selectedSportIds.Contains(sport.Id)))
            .ToArrayAsync(cancellationToken);

        model.AvailableSports = availableSports;
        model.AvailableTeamRoleOptions = availableTeamRoleOptions;
        model.AvailableGeographicScopeOptions = TeamGeographicScopeOptions;
        model.CreateType = string.IsNullOrWhiteSpace(model.CreateType)
            ? "team"
            : model.CreateType.Trim();
        model.ContactEmail = string.IsNullOrWhiteSpace(model.ContactEmail)
            ? user.Email
            : model.ContactEmail;
        model.ContactPhone = string.IsNullOrWhiteSpace(model.ContactPhone)
            ? user.PhoneNumber
            : model.ContactPhone;
        model.City = string.IsNullOrWhiteSpace(model.City)
            ? user.City
            : model.City;
        model.State = string.IsNullOrWhiteSpace(model.State)
            ? user.State
            : model.State;
        model.ZipCode = string.IsNullOrWhiteSpace(model.ZipCode)
            ? user.ZipCode
            : model.ZipCode;
        if (string.IsNullOrWhiteSpace(model.TeamRole))
        {
            model.TeamRole = availableTeamRoleOptions.FirstOrDefault() ?? string.Empty;
        }

        model.GeographicScope = NormalizeTeamGeographicScope(model.GeographicScope)
            ?? TeamGeographicScopeOptions[0];

        return model;
    }

    private async Task<Subscription> FindOrCreateAccountScopeSubscriptionAsync(
        User user,
        string planCode,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var subscription = await dbContext.Subscriptions
            .SingleOrDefaultAsync(currentSubscription =>
                currentSubscription.UserId == user.Id
                && currentSubscription.PlanType == planCode
                && currentSubscription.ScopeType == TryOutSpotSubscriptionScopeTypes.Account
                && currentSubscription.ScopeId == null,
                cancellationToken);
        if (subscription is not null)
        {
            return subscription;
        }

        subscription = new Subscription
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            PlanType = planCode,
            ScopeType = TryOutSpotSubscriptionScopeTypes.Account,
            ScopeId = null,
            Status = "plan_selected",
            Currency = "USD",
            BillingInterval = BillingIntervalCodes.Month,
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc
        };
        dbContext.Subscriptions.Add(subscription);
        return subscription;
    }

    private async Task<string?> ResolveExistingStripeCustomerIdAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Subscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .Where(subscription => subscription.StripeCustomerId != null && subscription.StripeCustomerId != string.Empty)
            .OrderByDescending(subscription =>
                TryOutSpotBillingCatalog.IsEntitlingSubscriptionStatus(subscription.Status))
            .ThenByDescending(subscription => subscription.UpdatedAt)
            .Select(subscription => subscription.StripeCustomerId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private static bool HasSelectedAnyRecommendedPlan(
        IEnumerable<Subscription> subscriptions,
        IReadOnlyCollection<BillingPlanResponse> recommendedPlans)
    {
        var planCodes = recommendedPlans
            .Select(plan => plan.Code)
            .ToHashSet(StringComparer.Ordinal);

        return subscriptions.Any(subscription =>
        {
            var normalizedPlanCode = TryOutSpotBillingCatalog.NormalizePlanCode(subscription.PlanType);
            return normalizedPlanCode is not null && planCodes.Contains(normalizedPlanCode);
        });
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
        var subscriptions = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(currentSubscription => currentSubscription.UserId == user.Id)
            .OrderByDescending(currentSubscription => currentSubscription.UpdatedAt)
            .ToArrayAsync(cancellationToken);
        var activePaidSubscriptions = subscriptions.Where(IsActivePaidSubscription).ToArray();
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
            CurrentPlanName = FormatCurrentPlanName(activePaidSubscriptions),
            CurrentPlanStatus = FormatCurrentPlanStatus(activePaidSubscriptions),
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
        return TryOutSpotBillingCatalog.GetEligiblePlanCodesForAccountTypes(
                accountTypes,
                includePlayerParentDefaultsWhenNoAccountTypes: true)
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

    private static bool IsActivePaidSubscription(Subscription subscription)
    {
        var plan = TryOutSpotBillingCatalog.GetPlan(subscription.PlanType);
        return plan?.RequiresStripeSubscription == true
            && TryOutSpotBillingCatalog.IsEntitlingSubscriptionStatus(subscription.Status);
    }

    private static string FormatCurrentPlanName(IReadOnlyCollection<Subscription> activePaidSubscriptions)
    {
        if (activePaidSubscriptions.Count == 0)
        {
            return "Free Player/Parent";
        }

        if (activePaidSubscriptions.Count == 1)
        {
            var subscription = activePaidSubscriptions.Single();
            return TryOutSpotBillingCatalog.GetPlan(subscription.PlanType)?.Name ?? subscription.PlanType;
        }

        return $"{activePaidSubscriptions.Count} active memberships";
    }

    private static string? FormatCurrentPlanStatus(IReadOnlyCollection<Subscription> activePaidSubscriptions)
    {
        if (activePaidSubscriptions.Count == 0)
        {
            return null;
        }

        if (activePaidSubscriptions.Count == 1)
        {
            return activePaidSubscriptions.Single().Status;
        }

        var planNames = activePaidSubscriptions
            .Select(subscription => TryOutSpotBillingCatalog.GetPlan(subscription.PlanType)?.Name ?? subscription.PlanType)
            .OrderBy(planName => planName)
            .ToArray();
        return string.Join(", ", planNames);
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

    private static string? SerializeLinkCollection(params (string Key, string? Value)[] links)
    {
        var populatedLinks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in links)
        {
            var normalizedValue = NormalizeOptional(value);
            if (normalizedValue is null)
            {
                continue;
            }

            populatedLinks[key] = normalizedValue;
        }

        return populatedLinks.Count == 0
            ? null
            : JsonSerializer.Serialize(populatedLinks);
    }

    private static string? NormalizeSocialHandleOrUrl(string? value, string baseUrl)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return null;
        }

        if (Uri.TryCreate(normalized, UriKind.Absolute, out var parsedUri)
            && (parsedUri.Scheme == Uri.UriSchemeHttp || parsedUri.Scheme == Uri.UriSchemeHttps))
        {
            return normalized;
        }

        var handle = normalized
            .Trim()
            .TrimStart('@')
            .TrimStart('/');
        return string.IsNullOrWhiteSpace(handle)
            ? null
            : $"{baseUrl}{handle}";
    }

    private static string? NormalizeRelationship(string? relationship)
    {
        if (string.IsNullOrWhiteSpace(relationship))
        {
            return null;
        }

        var trimmed = relationship.Trim();
        return PlayerRelationshipOptions.Contains(trimmed, StringComparer.OrdinalIgnoreCase)
            ? PlayerRelationshipOptions.First(option => string.Equals(option, trimmed, StringComparison.OrdinalIgnoreCase))
            : trimmed;
    }

    private static string? NormalizeTeamCreateType(string? createType)
    {
        if (string.IsNullOrWhiteSpace(createType))
        {
            return null;
        }

        return string.Equals(createType.Trim(), "organization", StringComparison.OrdinalIgnoreCase)
            ? "organization"
            : string.Equals(createType.Trim(), "team", StringComparison.OrdinalIgnoreCase)
                ? "team"
                : null;
    }

    private static string? NormalizeTeamOnboardingRole(
        string? role,
        IReadOnlyCollection<string> availableTeamRoleOptions)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        var normalizedRole = TeamOnboardingRoleOptions
            .FirstOrDefault(knownRole => string.Equals(knownRole, role.Trim(), StringComparison.OrdinalIgnoreCase));
        if (normalizedRole is null)
        {
            return null;
        }

        return availableTeamRoleOptions.Contains(normalizedRole, StringComparer.OrdinalIgnoreCase)
            ? normalizedRole
            : null;
    }

    private static string? NormalizePlayerContactVisibility(string? contactVisibility)
    {
        if (string.IsNullOrWhiteSpace(contactVisibility))
        {
            return null;
        }

        return PlayerContactVisibilityOptions.FirstOrDefault(
            option => string.Equals(option, contactVisibility.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string? NormalizeTeamGeographicScope(string? geographicScope)
    {
        if (string.IsNullOrWhiteSpace(geographicScope))
        {
            return null;
        }

        return TeamGeographicScopeOptions.FirstOrDefault(
            option => string.Equals(option, geographicScope.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static DateTime NormalizeUtcDate(DateTime value)
    {
        return DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
    }

    private static string? NormalizeState(string? state)
    {
        return string.IsNullOrWhiteSpace(state) ? null : state.Trim().ToUpperInvariant();
    }
}
