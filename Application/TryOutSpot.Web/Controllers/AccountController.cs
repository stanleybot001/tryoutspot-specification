using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Globalization;
using System.Text.Json;
using System.Security.Claims;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Listings;
using TryOutSpot.Web.Models.Billing;
using TryOutSpot.Web.Models.Dashboard;
using TryOutSpot.Web.Models.Listings;
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
    ILogger<AccountController> logger,
    IAccountEmailSender accountEmailSender,
    IAccountSmsSender accountSmsSender,
    IEntitlementService entitlementService,
    IDashboardActivityService dashboardActivityService,
    IZipRadiusSearchService zipRadiusSearchService,
    IPdfStorageService pdfStorageService,
    IImageStorageService imageStorageService,
    IExternalLoginTicketService externalLoginTicketService,
    IStripeBillingService stripeBillingService,
    IStripeSubscriptionSyncService stripeSubscriptionSyncService,
    IAccountTypeChangeWorkflowService accountTypeChangeWorkflowService,
    ILaunchPromotionStatusService launchPromotionStatusService,
    IOptions<GoogleAuthenticationOptions> googleOptions,
    IOptions<StripeBillingOptions> stripeOptions) : Controller
{
    private const int FreeCoachPublishingLimit = 1;
    private const int FreeCoachPublishingWindowMonths = 6;
    private const int BasicTeamPublishingLimit = 9;
    private const int BasicTeamPublishingWindowMonths = 12;
    private const int ProfessionalTeamPublishingLimit = 24;
    private const int ProfessionalTeamPublishingWindowMonths = 12;
    private const int EnterpriseTeamPublishingLimit = 50;
    private const int EnterpriseTeamPublishingWindowMonths = 12;
    private const int DefaultDashboardRecentActivityPageSize = 8;
    private const int MaxDashboardRecentActivityPageSize = 25;
    private const int DashboardFavoritePreviewLimit = 12;
    private const int DefaultFavoritePageSize = 25;
    private const int FavoriteListLimit = 100;
    private const int DefaultSearchPageSize = 20;
    private const int MaxSearchPageSize = 50;
    private static readonly ListingReportReasonOptionPageItem[] ListingReportReasonOptions =
    [
        new("Inappropriate content", "Inappropriate content"),
        new("Misleading listing", "Misleading listing"),
        new("Spam or scam", "Spam or scam"),
        new("Safety concern", "Safety concern"),
        new("Other", "Other")
    ];
    private const int FreeSearchMaxRadiusMiles = 120;
    private const int SearchSuggestionResultLimit = 5;
    private const string AllSearchFilterValue = "all";
    private const int ListingFlyerMaxSizeMegabytes = 10;
    private const long ListingFlyerMaxSizeBytes = ListingFlyerMaxSizeMegabytes * 1024L * 1024L;
    private const string PlayerListingDocumentType = "player-listings";
    private const string OpportunityDocumentType = "opportunities";
    private const string OpportunityWaiverDocumentType = "opportunity-waivers";
    private const string TeamLogoDocumentType = "team-logos";
    private const string PlayerProfileImageDocumentType = "player-profile-images";
    private const string RegistrationWorkflowStatusPending = "pending";
    private const string RegistrationWorkflowStatusAccepted = "accepted";
    private const string RegistrationWorkflowStatusDeclined = "declined";
    private const string RegistrationAttendancePresentStatus = "present";
    private const string RegistrationAttendancePendingStatus = "pending";
    private const string R2ObjectStoragePrefix = "r2:";
    private const int ProfileImageMaxSizeMegabytes = 2;
    private const long ProfileImageMaxSizeBytes = ProfileImageMaxSizeMegabytes * 1024L * 1024L;
    private static readonly string[] SupportedImageContentTypes =
    [
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/svg+xml"
    ];
    private static readonly string[] SupportedListingFlyerContentTypes =
    [
        "application/pdf",
        "image/jpeg",
        "image/png",
        "image/webp"
    ];
    private static readonly (string Token, string BrowserDisplayName)[] InAppBrowserSignatures =
    [
        ("TwitterAndroid", "the X app browser"),
        ("Twitter for iPhone", "the X app browser"),
        ("Twitter", "the X app browser"),
        ("FBAN/Messenger", "the Messenger app browser"),
        ("MessengerForiOS", "the Messenger app browser"),
        ("FB_IAB/MESSENGER", "the Messenger app browser"),
        ("Instagram", "the Instagram app browser"),
        ("FB_IAB", "the Facebook app browser"),
        ("FBAN", "the Facebook app browser"),
        ("FBAV", "the Facebook app browser"),
        ("FBIOS", "the Facebook app browser"),
        ("FB4A", "the Facebook app browser"),
        ("TikTok", "the TikTok app browser"),
        ("BytedanceWebview", "the TikTok app browser"),
        ("musical_ly", "the TikTok app browser"),
        ("LinkedInApp", "the LinkedIn app browser"),
        ("LinkedIn", "the LinkedIn app browser"),
        ("Pinterest", "the Pinterest app browser"),
        ("Snapchat", "the Snapchat app browser"),
        ("Discord", "the Discord app browser"),
        ("Reddit", "the Reddit app browser"),
        ("Line/", "the LINE app browser"),
        ("MicroMessenger", "the WeChat app browser")
    ];
    private readonly GoogleAuthenticationOptions googleAuthentication = googleOptions.Value;
    private readonly StripeBillingOptions stripeBillingOptions = stripeOptions.Value;
    private static readonly string[] PlayerRelationshipOptions = ["Parent", "Guardian", "Self", "Coach", "Other"];
    private static readonly string[] PlayerContactVisibilityOptions = ["Public", "VerifiedCoachesOnly"];
    private static readonly string[] TeamOnboardingRoleOptions =
    [
        TryOutSpotRoles.TeamRepresentative
    ];
    private static readonly string[] TeamGeographicScopeOptions = ["Local", "Regional", "National"];
    private static readonly string[] TeamOpportunityTypeOptions =
    [
        "tryout",
        "roster_opening",
        "pickup_player",
        "camp",
        "clinic",
        "tournament",
        "private_workout"
    ];
    private static readonly int[] SearchRadiusOptions = [10, 25, 30, 60, 120, 250];
    private static readonly int[] SearchSuggestionRadiusMiles = [30, 60, 120, 250];
    private static readonly PlayerListingTypeSelectionPageItem[] PlayerListingTypeOptions =
    [
        new(
            TryOutSpotPlayerListingTypes.PickupPlayer,
            "Pickup player availability",
            "Post when a player is available to fill in for games, tournaments, or weekend events.",
            RequiresPlayerSelection: true,
            RequiresSportSelection: true,
            SupportsCondition: false,
            SupportsAskingPrice: false),
        new(
            TryOutSpotPlayerListingTypes.LookingForTeam,
            "Looking for a team",
            "Share that a player is looking for a new team, coach, or roster opening.",
            RequiresPlayerSelection: true,
            RequiresSportSelection: true,
            SupportsCondition: false,
            SupportsAskingPrice: false),
        new(
            TryOutSpotPlayerListingTypes.UsedEquipment,
            "Used equipment",
            "List used gear that families can sell to others in the community.",
            RequiresPlayerSelection: false,
            RequiresSportSelection: false,
            SupportsCondition: true,
            SupportsAskingPrice: true),
        new(
            TryOutSpotPlayerListingTypes.WantedEquipment,
            "Wanted equipment",
            "Post gear requests when a player is searching for equipment to buy.",
            RequiresPlayerSelection: false,
            RequiresSportSelection: false,
            SupportsCondition: false,
            SupportsAskingPrice: true),
        new(
            TryOutSpotPlayerListingTypes.PrivateLessons,
            "Private lessons",
            "Post private lesson needs or coaching availability tied to a player and sport.",
            RequiresPlayerSelection: true,
            RequiresSportSelection: true,
            SupportsCondition: false,
            SupportsAskingPrice: true),
        new(
            TryOutSpotPlayerListingTypes.TrainingPartner,
            "Training partner",
            "Find local training partners for drills, workouts, and shared practice sessions.",
            RequiresPlayerSelection: true,
            RequiresSportSelection: true,
            SupportsCondition: false,
            SupportsAskingPrice: false)
    ];
    private static readonly PlayerListingVisibilityOptionPageItem[] PlayerListingSocialDisplayOptions =
    [
        new("facebook", "Facebook page"),
        new("x", "X profile"),
        new("instagram", "Instagram profile"),
        new("youtube", "YouTube channel"),
        new("tiktok", "TikTok profile")
    ];
    private static readonly PlayerListingVisibilityOptionPageItem[] PlayerListingVideoDisplayOptions =
    [
        new("highlight_video_1", "Highlight video 1"),
        new("highlight_video_2", "Highlight video 2")
    ];

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
        var existingUser = await userManager.FindByEmailAsync(email);
        if (existingUser is not null)
        {
            ModelState.AddModelError(
                string.Empty,
                await BuildExistingAccountRegistrationMessageAsync(existingUser));
            return View(PrepareRegisterModel(model));
        }

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

    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerificationEmail(
        [FromForm] string? email,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        await SendVerificationEmailIfEligibleAsync(normalizedEmail, cancellationToken);

        TempData["StatusMessage"] = "If an active unverified account exists for that email address, verification instructions were sent.";
        return RedirectToAction(nameof(RegisterConfirmation), new { email = normalizedEmail });
    }

    [HttpPost("login/resend-verification")]
    public async Task<IActionResult> ResendVerificationEmailFromLogin(
        [FromForm] string? email,
        [FromForm] string? returnUrl,
        CancellationToken cancellationToken)
    {
        var normalizedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        await SendVerificationEmailIfEligibleAsync(normalizedEmail, cancellationToken);

        TempData["StatusMessage"] = "If an active unverified account exists for that email address, verification instructions were sent.";
        return RedirectToAction(nameof(Login), new
        {
            returnUrl,
            email = normalizedEmail
        });
    }

    [HttpGet("login")]
    public async Task<IActionResult> Login(
        [FromQuery] string? returnUrl = null,
        [FromQuery] string? email = null,
        [FromQuery] string? externalLoginStatus = null)
    {
        if (string.Equals(externalLoginStatus, "failed", StringComparison.OrdinalIgnoreCase))
        {
            TempData["StatusMessage"] = "The social login session expired or could not be verified. Please start social sign-in again.";
        }

        var model = new LoginPageModel
        {
            ReturnUrl = returnUrl,
            Email = string.IsNullOrWhiteSpace(email) ? string.Empty : email.Trim()
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

        var passwordlessSocialAccount = await GetPasswordlessSocialAccountInfoAsync(user);
        if (passwordlessSocialAccount is not null)
        {
            ModelState.AddModelError(
                string.Empty,
                BuildPasswordlessSocialLoginMessage(passwordlessSocialAccount.ProviderNames));
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
            model.ShowResendVerificationPrompt = true;
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
    [IgnoreAntiforgeryToken]
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
    public async Task<IActionResult> ExternalLogin(
        [FromQuery] string provider,
        [FromQuery] string? returnUrl = null,
        [FromQuery] string? source = null)
    {
        var normalizedProvider = TryOutSpotSocialLoginProviders.Normalize(provider);
        if (normalizedProvider is null)
        {
            TempData["StatusMessage"] = "That social login provider is not supported.";
            return RedirectToAction(nameof(Login));
        }

        var availability = await GetExternalProviderAvailabilityAsync();
        if (!IsExternalProviderConfigured(normalizedProvider, availability))
        {
            TempData["StatusMessage"] = $"{GetProviderDisplayName(normalizedProvider)} login is not configured yet.";
            return RedirectToAction(nameof(Login));
        }

        var inAppBrowser = DetectBlockedExternalLoginBrowser(normalizedProvider, Request.Headers.UserAgent.ToString());
        if (inAppBrowser is not null)
        {
            var fallbackPath = GetExternalLoginFallbackPath(source, returnUrl);
            var fallbackUrl = BuildAbsoluteUrl(fallbackPath);
            return View("ExternalLoginBrowserWarning", new ExternalLoginBrowserWarningPageModel
            {
                Provider = normalizedProvider,
                ProviderDisplayName = GetProviderDisplayName(normalizedProvider),
                BrowserDisplayName = inAppBrowser.BrowserDisplayName,
                ContinueInBrowserUrl = fallbackUrl,
                EmailFallbackUrl = fallbackPath,
                AndroidChromeIntentUrl = inAppBrowser.IsAndroid
                    ? BuildAndroidChromeIntentUrl(fallbackUrl)
                    : null
            });
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
    public async Task<IActionResult> Onboarding(
        [FromQuery] int activityPage = 1,
        [FromQuery] int activityPageSize = DefaultDashboardRecentActivityPageSize,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(Onboarding)) });
        }

        return View(await BuildOnboardingPageModelAsync(
            user,
            NormalizeDashboardActivityPage(activityPage),
            NormalizeDashboardActivityPageSize(activityPageSize),
            cancellationToken));
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpGet("onboarding/coach-getting-started")]
    public async Task<IActionResult> CoachGettingStarted(CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(CoachGettingStarted)) });
        }

        return View(await BuildCoachGettingStartedPageModelAsync(user, cancellationToken));
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpPost("onboarding/recent-activity/reset")]
    public async Task<IActionResult> ResetDashboardActivityWindow(CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(Onboarding)) });
        }

        await dashboardActivityService.MarkViewedAsync(user.Id, cancellationToken);
        TempData["StatusMessage"] = "What's new will start from this moment the next time new items are added.";
        return RedirectToAction(nameof(Onboarding));
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpGet("onboarding/favorites")]
    public async Task<IActionResult> Favorites(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = DefaultFavoritePageSize,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(Favorites)) });
        }

        return View(await BuildFavoritesPageModelAsync(user, page, pageSize, cancellationToken));
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ActiveUser)]
    [HttpGet("search/team-items")]
    public async Task<IActionResult> SearchTeamItems(
        [FromQuery] SearchTeamItemsPageModel model,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(SearchTeamItems)) });
        }

        if (Request.Query.Count == 0 && string.IsNullOrWhiteSpace(model.OriginZipCode))
        {
            model.OriginZipCode = user.ZipCode;
        }

        var preparedModel = await BuildSearchTeamItemsPageModelAsync(user, model, cancellationToken);
        if (!preparedModel.CanSearchTeamItems)
        {
            TempData["StatusMessage"] = "Search Team Items is available to parent and player accounts.";
            return RedirectToAction(nameof(Onboarding));
        }

        return View(preparedModel);
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ActiveUser)]
    [HttpGet("search/players")]
    public async Task<IActionResult> SearchPlayers(
        [FromQuery] SearchPlayersPageModel model,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(SearchPlayers)) });
        }

        if (Request.Query.Count == 0 && string.IsNullOrWhiteSpace(model.OriginZipCode))
        {
            model.OriginZipCode = user.ZipCode;
        }

        var preparedModel = await BuildSearchPlayersPageModelAsync(user, model, cancellationToken);
        if (!preparedModel.CanSearchPlayers)
        {
            TempData["StatusMessage"] = "Search Players is available to team representative accounts.";
            return RedirectToAction(nameof(Onboarding));
        }

        return View(preparedModel);
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpPost("onboarding/account-types")]
    public async Task<IActionResult> UpdateOnboardingAccountTypes(
        [FromForm] List<string>? accountTypes,
        [FromForm] string? playerParentRole,
        [FromForm] string? teamRole,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(Onboarding)) });
        }

        var normalizationMessage = await NormalizeCurrentUserPublicRoleBundlesAsync(user, cancellationToken);
        var requestedAccountTypes = BuildRequestedAccountTypesFromBundles(accountTypes, playerParentRole, teamRole);
        var validatedAccountTypes = GetValidatedPublicAccountTypes(requestedAccountTypes, nameof(accountTypes));
        var result = await accountTypeChangeWorkflowService.QueueOrApplyAsync(user, validatedAccountTypes, cancellationToken);
        if (result.AppliedImmediately)
        {
            await SignInWebUserAsync(user, isPersistent: true);
        }
        TempData["StatusMessage"] = string.IsNullOrWhiteSpace(normalizationMessage)
            ? result.Message
            : $"{normalizationMessage} {result.Message}";
        return RedirectToAction(nameof(Onboarding));
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpPost("promotions/launch-founder-offer/claim")]
    public async Task<IActionResult> ClaimLaunchFounderOfferPromotion(CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(Onboarding)) });
        }

        if (!user.EmailConfirmed)
        {
            TempData["StatusMessage"] = "Verify your email address before claiming the founder offer.";
            return RedirectToAction(nameof(Onboarding));
        }

        var roles = await GetCanonicalPublicRolesAsync(user);
        var result = await launchPromotionStatusService.ClaimLaunchFounderOfferAsync(
            user.Id,
            roles,
            cancellationToken);

        TempData["StatusMessage"] = BuildLaunchPromotionClaimMessage(result);
        return RedirectToAction(nameof(Onboarding));
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManagePlayerProfile)]
    [HttpGet("onboarding/player-profiles")]
    public async Task<IActionResult> ManagePlayerProfiles(CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(ManagePlayerProfiles)) });
        }

        var model = await BuildManagePlayerProfilesPageModelAsync(user, cancellationToken);
        return View(model);
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManagePlayerProfile)]
    [HttpGet("onboarding/add-player-profile")]
    public async Task<IActionResult> AddPlayerProfile(
        [FromQuery] bool createNew = false,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(AddPlayerProfile)) });
        }

        if (!createNew)
        {
            var hasManagedProfiles = await dbContext.UserPlayerRelationships
                .AsNoTracking()
                .AnyAsync(
                    relationship => relationship.UserId == user.Id
                        && relationship.CanManage
                        && relationship.Player.IsActive,
                    cancellationToken);
            if (hasManagedProfiles)
            {
                return RedirectToAction(nameof(ManagePlayerProfiles));
            }
        }

        var model = await BuildAddPlayerProfilePageModelAsync(
            user,
            new AddPlayerProfilePageModel
            {
                IsEditMode = false,
                ReturnUrl = Url.Action(nameof(ManagePlayerProfiles)),
                ContactEmail = user.Email,
                ContactPhone = user.PhoneNumber,
                City = user.City,
                State = user.State,
                ZipCode = user.ZipCode,
                IsSearchable = false,
                DateOfBirth = DateTime.UtcNow.Date.AddYears(-12)
            },
            cancellationToken);
        return View(model);
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManagePlayerProfile)]
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

        model.IsEditMode = false;
        model.ReturnUrl ??= Url.Action(nameof(ManagePlayerProfiles));

        var validationContext = await ValidatePlayerProfileInputAsync(model, cancellationToken);
        if (!ModelState.IsValid || validationContext is null)
        {
            return View(await BuildAddPlayerProfilePageModelAsync(user, model, cancellationToken));
        }

        var uploadedProfileImage = await ParseUploadedImageAsync(
            model.ProfileImageUpload,
            nameof(model.ProfileImageUpload),
            cancellationToken);
        if (!ModelState.IsValid)
        {
            return View(await BuildAddPlayerProfilePageModelAsync(user, model, cancellationToken));
        }

        var now = DateTime.UtcNow;
        var playerId = Guid.NewGuid();
        var player = new Player
        {
            Id = playerId,
            CreatedAt = now,
            IsActive = true
        };
        ApplyPlayerProfileValues(player, model, validationContext.ContactVisibility, now);
        if (uploadedProfileImage is not null)
        {
            var objectKey = BuildImageObjectKey(PlayerProfileImageDocumentType, playerId, user.Id, uploadedProfileImage.FileName);
            await imageStorageService.UploadImageAsync(objectKey, uploadedProfileImage.Content, uploadedProfileImage.ContentType, cancellationToken);
            player.ProfileImageUrl = ToStoredObjectReference(objectKey);
        }

        var relationshipToUser = new UserPlayerRelationship
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            PlayerId = playerId,
            Relationship = validationContext.Relationship,
            CanManage = model.CanManage,
            CreatedAt = now
        };

        dbContext.Players.Add(player);
        dbContext.UserPlayerRelationships.Add(relationshipToUser);

        var selectedSportSet = validationContext.SelectedSportIds.ToHashSet();
        foreach (var sportDetail in validationContext.SelectedSportDetails)
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
                ExperienceLevel = NormalizeOptional(sportDetail.ExperienceLevel),
                YearsPlaying = sportDetail.YearsPlaying,
                Availability = NormalizeOptional(sportDetail.Availability),
                IsActive = true,
                CreatedAt = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = "Player profile added.";
        return RedirectToAction(nameof(ManagePlayerProfiles));
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManagePlayerProfile)]
    [HttpGet("onboarding/player-profiles/{playerId:guid}/edit")]
    public async Task<IActionResult> EditPlayerProfile(Guid playerId, CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(EditPlayerProfile), new { playerId })
            });
        }

        var model = await BuildEditablePlayerProfilePageModelAsync(user.Id, playerId, cancellationToken);
        if (model is null)
        {
            TempData["StatusMessage"] = "Player profile was not found for this account.";
            return RedirectToAction(nameof(ManagePlayerProfiles));
        }

        model = await BuildAddPlayerProfilePageModelAsync(user, model, cancellationToken);
        return View("AddPlayerProfile", model);
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManagePlayerProfile)]
    [HttpPost("onboarding/player-profiles/{playerId:guid}/edit")]
    public async Task<IActionResult> EditPlayerProfile(
        Guid playerId,
        AddPlayerProfilePageModel model,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(EditPlayerProfile), new { playerId })
            });
        }

        var relationshipToUser = await dbContext.UserPlayerRelationships
            .Include(relationship => relationship.Player)
            .SingleOrDefaultAsync(
                relationship => relationship.UserId == user.Id
                    && relationship.PlayerId == playerId
                    && relationship.CanManage
                    && relationship.Player.IsActive,
                cancellationToken);
        if (relationshipToUser is null)
        {
            TempData["StatusMessage"] = "Player profile was not found for this account.";
            return RedirectToAction(nameof(ManagePlayerProfiles));
        }

        model.PlayerId = playerId;
        model.IsEditMode = true;
        model.ReturnUrl ??= Url.Action(nameof(ManagePlayerProfiles));

        var validationContext = await ValidatePlayerProfileInputAsync(model, cancellationToken);
        if (!ModelState.IsValid || validationContext is null)
        {
            return View("AddPlayerProfile", await BuildAddPlayerProfilePageModelAsync(user, model, cancellationToken));
        }

        var uploadedProfileImage = await ParseUploadedImageAsync(
            model.ProfileImageUpload,
            nameof(model.ProfileImageUpload),
            cancellationToken);
        if (!ModelState.IsValid)
        {
            return View("AddPlayerProfile", await BuildAddPlayerProfilePageModelAsync(user, model, cancellationToken));
        }

        var now = DateTime.UtcNow;
        var player = relationshipToUser.Player;
        if (uploadedProfileImage is not null || model.RemoveProfileImage)
        {
            await ReplacePlayerProfileImageAsync(
                player,
                playerId,
                user.Id,
                uploadedProfileImage,
                model.RemoveProfileImage,
                cancellationToken);
        }

        ApplyPlayerProfileValues(player, model, validationContext.ContactVisibility, now);
        relationshipToUser.Relationship = validationContext.Relationship;
        relationshipToUser.CanManage = model.CanManage;

        var selectedSportIds = validationContext.SelectedSportIds.ToHashSet();
        var selectedSportDetails = validationContext.SelectedSportDetails
            .ToDictionary(detail => detail.SportId, detail => detail);
        var currentSports = await dbContext.PlayerSports
            .Where(playerSport => playerSport.PlayerId == playerId)
            .ToArrayAsync(cancellationToken);

        foreach (var playerSport in currentSports)
        {
            if (selectedSportIds.Contains(playerSport.SportId))
            {
                var selectedDetail = selectedSportDetails[playerSport.SportId];
                playerSport.SkillLevel = NormalizeOptional(selectedDetail.SkillLevel);
                playerSport.PrimaryPosition = NormalizeOptional(selectedDetail.PrimaryPosition);
                playerSport.SecondaryPositions = NormalizeOptional(selectedDetail.SecondaryPositions);
                playerSport.ExperienceLevel = NormalizeOptional(selectedDetail.ExperienceLevel);
                playerSport.YearsPlaying = selectedDetail.YearsPlaying;
                playerSport.Availability = NormalizeOptional(selectedDetail.Availability);
                playerSport.IsActive = true;
            }
            else
            {
                playerSport.IsActive = false;
            }
        }

        var existingSportIds = currentSports
            .Select(playerSport => playerSport.SportId)
            .ToHashSet();
        foreach (var sportDetail in validationContext.SelectedSportDetails)
        {
            if (existingSportIds.Contains(sportDetail.SportId))
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
                ExperienceLevel = NormalizeOptional(sportDetail.ExperienceLevel),
                YearsPlaying = sportDetail.YearsPlaying,
                Availability = NormalizeOptional(sportDetail.Availability),
                IsActive = true,
                CreatedAt = now
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = "Player profile updated.";
        return RedirectToAction(nameof(ManagePlayerProfiles));
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManagePlayerProfile)]
    [HttpPost("onboarding/player-profiles/{playerId:guid}/deactivate")]
    public async Task<IActionResult> DeactivatePlayerProfile(Guid playerId, CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(ManagePlayerProfiles)) });
        }

        var relationshipToUser = await dbContext.UserPlayerRelationships
            .Include(relationship => relationship.Player)
            .SingleOrDefaultAsync(
                relationship => relationship.UserId == user.Id
                    && relationship.PlayerId == playerId
                    && relationship.CanManage
                    && relationship.Player.IsActive,
                cancellationToken);
        if (relationshipToUser is null)
        {
            TempData["StatusMessage"] = "Player profile was not found for this account.";
            return RedirectToAction(nameof(ManagePlayerProfiles));
        }

        var now = DateTime.UtcNow;
        var player = relationshipToUser.Player;
        player.IsActive = false;
        player.IsSearchable = false;
        player.UpdatedAt = now;

        var playerSports = await dbContext.PlayerSports
            .Where(playerSport => playerSport.PlayerId == playerId && playerSport.IsActive)
            .ToArrayAsync(cancellationToken);
        foreach (var playerSport in playerSports)
        {
            playerSport.IsActive = false;
        }

        var playerListings = await dbContext.PlayerListings
            .Where(listing => listing.UserId == user.Id)
            .Where(listing => listing.PlayerId == playerId)
            .Where(listing => listing.IsActive)
            .ToArrayAsync(cancellationToken);
        foreach (var playerListing in playerListings)
        {
            playerListing.IsActive = false;
            playerListing.IsPublished = false;
            playerListing.PublishedAt = null;
            playerListing.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = "Player profile deactivated.";
        return RedirectToAction(nameof(ManagePlayerProfiles));
    }

    [AllowAnonymous]
    [HttpGet("/players/{playerId:guid}")]
    public async Task<IActionResult> PlayerProfileDetail(Guid playerId, CancellationToken cancellationToken = default)
    {
        var currentUser = await GetCurrentWebUserAsync();
        var player = await dbContext.Players
            .AsNoTracking()
            .Where(currentPlayer => currentPlayer.Id == playerId)
            .Where(currentPlayer => currentPlayer.IsActive)
            .Where(currentPlayer => currentPlayer.IsSearchable)
            .Include(currentPlayer => currentPlayer.PlayerSports)
                .ThenInclude(playerSport => playerSport.Sport)
            .SingleOrDefaultAsync(cancellationToken);
        if (player is null)
        {
            return NotFound();
        }

        var model = await BuildPlayerProfileDetailPageModelAsync(player, currentUser, cancellationToken);
        return View(model);
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.FeaturePolicyPrefix + TryOutSpotFeatureCodes.CreatePlayerListings)]
    [HttpGet("onboarding/player-listings")]
    public async Task<IActionResult> ManagePlayerListings(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(ManagePlayerListings), new { page, pageSize })
            });
        }

        var model = await BuildPlayerListingListPageModelAsync(user, page, pageSize, cancellationToken);
        return View(model);
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.FeaturePolicyPrefix + TryOutSpotFeatureCodes.CreatePlayerListings)]
    [HttpGet("onboarding/player-listings/new")]
    public async Task<IActionResult> CreatePlayerListing(CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(CreatePlayerListing)) });
        }

        var model = await BuildPlayerListingEditorPageModelAsync(user, null, null, cancellationToken);
        return View("EditPlayerListing", model);
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.FeaturePolicyPrefix + TryOutSpotFeatureCodes.CreatePlayerListings)]
    [HttpPost("onboarding/player-listings/new")]
    public async Task<IActionResult> CreatePlayerListing(
        PlayerListingEditorPageModel model,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(CreatePlayerListing)) });
        }

        var preparedModel = await BuildPlayerListingEditorPageModelAsync(user, null, model, cancellationToken);
        if (preparedModel is null)
        {
            TempData["StatusMessage"] = "Listing setup could not be loaded.";
            return RedirectToAction(nameof(ManagePlayerListings));
        }

        model = preparedModel;
        var validationContext = await ValidatePlayerListingEditorInputAsync(user, model, cancellationToken);
        if (!ModelState.IsValid || validationContext.ListingTypeOption is null)
        {
            return View("EditPlayerListing", model);
        }

        var now = DateTime.UtcNow;
        var listing = new PlayerListing
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            PlayerId = validationContext.Player?.Id,
            SportId = model.SportId,
            ListingType = validationContext.ListingTypeOption.Code,
            Title = model.Title.Trim(),
            Description = NormalizeOptional(model.Description),
            AskingPrice = validationContext.ListingTypeOption.SupportsAskingPrice ? model.AskingPrice : null,
            Currency = validationContext.ListingTypeOption.SupportsAskingPrice
                ? NormalizeCurrency(model.Currency) ?? "USD"
                : null,
            Condition = validationContext.ListingTypeOption.SupportsCondition ? NormalizeOptional(model.Condition) : null,
            City = NormalizeOptional(model.City) ?? validationContext.Player?.City ?? user.City,
            State = NormalizeState(model.State) ?? validationContext.Player?.State ?? user.State,
            ZipCode = ResolveZipCodeOrAddModelError(
                model.ZipCode,
                validationContext.Player?.ZipCode ?? user.ZipCode,
                nameof(model.ZipCode)),
            VisibleSocialLinkKeys = SerializePlayerListingVisibleSocialKeys(model.VisibleSocialLinkKeys),
            IsSearchable = model.IsSearchable,
            IsPublished = model.IsPublished,
            PublishedAt = model.IsPublished ? now : null,
            ExpiresAt = NormalizeUtc(model.ExpiresAt),
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };

        await ApplyPlayerListingPdfUploadChangesAsync(
            listing,
            user.Id,
            model.PdfUpload,
            model.RemoveUploadedPdf,
            nameof(model.PdfUpload),
            cancellationToken);

        if (!ModelState.IsValid)
        {
            return View("EditPlayerListing", model);
        }

        dbContext.PlayerListings.Add(listing);
        await dbContext.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = "Listing created.";
        return RedirectToAction(nameof(ManagePlayerListings));
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.FeaturePolicyPrefix + TryOutSpotFeatureCodes.CreatePlayerListings)]
    [HttpGet("onboarding/player-listings/{listingId:guid}/edit")]
    public async Task<IActionResult> EditPlayerListing(Guid listingId, CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(EditPlayerListing), new { listingId })
            });
        }

        var model = await BuildPlayerListingEditorPageModelAsync(user, listingId, null, cancellationToken);
        if (model is null)
        {
            TempData["StatusMessage"] = "Listing was not found for this account.";
            return RedirectToAction(nameof(ManagePlayerListings));
        }

        return View("EditPlayerListing", model);
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.FeaturePolicyPrefix + TryOutSpotFeatureCodes.CreatePlayerListings)]
    [HttpPost("onboarding/player-listings/{listingId:guid}/edit")]
    public async Task<IActionResult> EditPlayerListing(
        Guid listingId,
        PlayerListingEditorPageModel model,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(EditPlayerListing), new { listingId })
            });
        }

        var listing = await dbContext.PlayerListings
            .SingleOrDefaultAsync(currentListing =>
                currentListing.Id == listingId
                && currentListing.UserId == user.Id
                && currentListing.IsActive,
                cancellationToken);
        if (listing is null)
        {
            TempData["StatusMessage"] = "Listing was not found for this account.";
            return RedirectToAction(nameof(ManagePlayerListings));
        }

        model = await BuildPlayerListingEditorPageModelAsync(user, listingId, model, cancellationToken)
            ?? model;
        var validationContext = await ValidatePlayerListingEditorInputAsync(user, model, cancellationToken);
        if (!ModelState.IsValid || validationContext.ListingTypeOption is null)
        {
            return View("EditPlayerListing", model);
        }

        listing.PlayerId = validationContext.Player?.Id;
        listing.SportId = model.SportId;
        listing.ListingType = validationContext.ListingTypeOption.Code;
        listing.Title = model.Title.Trim();
        listing.Description = NormalizeOptional(model.Description);
        listing.AskingPrice = validationContext.ListingTypeOption.SupportsAskingPrice ? model.AskingPrice : null;
        listing.Currency = validationContext.ListingTypeOption.SupportsAskingPrice
            ? NormalizeCurrency(model.Currency) ?? "USD"
            : null;
        listing.Condition = validationContext.ListingTypeOption.SupportsCondition ? NormalizeOptional(model.Condition) : null;
        listing.City = NormalizeOptional(model.City) ?? validationContext.Player?.City ?? user.City;
        listing.State = NormalizeState(model.State) ?? validationContext.Player?.State ?? user.State;
        listing.ZipCode = ResolveZipCodeOrAddModelError(
            model.ZipCode,
            validationContext.Player?.ZipCode ?? user.ZipCode,
            nameof(model.ZipCode));
        listing.VisibleSocialLinkKeys = SerializePlayerListingVisibleSocialKeys(model.VisibleSocialLinkKeys);
        listing.IsSearchable = model.IsSearchable;
        listing.IsPublished = model.IsPublished;
        listing.PublishedAt = model.IsPublished
            ? listing.PublishedAt ?? DateTime.UtcNow
            : null;
        listing.ExpiresAt = NormalizeUtc(model.ExpiresAt);
        listing.UpdatedAt = DateTime.UtcNow;

        await ApplyPlayerListingPdfUploadChangesAsync(
            listing,
            user.Id,
            model.PdfUpload,
            model.RemoveUploadedPdf,
            nameof(model.PdfUpload),
            cancellationToken);

        if (!ModelState.IsValid)
        {
            return View("EditPlayerListing", model);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = "Listing updated.";
        return RedirectToAction(nameof(ManagePlayerListings));
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.FeaturePolicyPrefix + TryOutSpotFeatureCodes.CreatePlayerListings)]
    [HttpPost("onboarding/player-listings/{listingId:guid}/publication")]
    public async Task<IActionResult> SetPlayerListingPublication(
        Guid listingId,
        [FromForm] bool isPublished,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(ManagePlayerListings))
            });
        }

        var listing = await dbContext.PlayerListings
            .SingleOrDefaultAsync(currentListing =>
                currentListing.Id == listingId
                && currentListing.UserId == user.Id
                && currentListing.IsActive,
                cancellationToken);
        if (listing is null)
        {
            TempData["StatusMessage"] = "Listing was not found for this account.";
            return RedirectToAction(nameof(ManagePlayerListings));
        }

        if (isPublished && listing.ExpiresAt.HasValue && listing.ExpiresAt <= DateTime.UtcNow)
        {
            TempData["StatusMessage"] = "This listing has already expired. Extend the expiration date before publishing.";
            return RedirectToAction(nameof(ManagePlayerListings));
        }

        var now = DateTime.UtcNow;
        listing.IsPublished = isPublished;
        listing.PublishedAt = isPublished
            ? listing.PublishedAt ?? now
            : null;
        listing.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = isPublished ? "Listing published." : "Listing unpublished.";
        return RedirectToAction(nameof(ManagePlayerListings));
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.FeaturePolicyPrefix + TryOutSpotFeatureCodes.CreatePlayerListings)]
    [HttpPost("onboarding/player-listings/{listingId:guid}/deactivate")]
    public async Task<IActionResult> DeactivatePlayerListing(
        Guid listingId,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(ManagePlayerListings))
            });
        }

        var listing = await dbContext.PlayerListings
            .SingleOrDefaultAsync(currentListing =>
                currentListing.Id == listingId
                && currentListing.UserId == user.Id
                && currentListing.IsActive,
                cancellationToken);
        if (listing is null)
        {
            TempData["StatusMessage"] = "Listing was not found for this account.";
            return RedirectToAction(nameof(ManagePlayerListings));
        }

        listing.IsActive = false;
        listing.IsPublished = false;
        listing.PublishedAt = null;
        listing.UpdatedAt = DateTime.UtcNow;

        await RemovePlayerListingPdfAsync(listing, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = "Listing deactivated.";
        return RedirectToAction(nameof(ManagePlayerListings));
    }

    [AllowAnonymous]
    [HttpGet("/player-listings/{listingId:guid}")]
    public async Task<IActionResult> PlayerListingDetail(Guid listingId, CancellationToken cancellationToken = default)
    {
        var currentUser = await GetCurrentWebUserAsync();
        var now = DateTime.UtcNow;
        var listing = await dbContext.PlayerListings
            .AsNoTracking()
            .Where(currentListing => currentListing.Id == listingId)
            .Where(currentListing => currentListing.IsActive)
            .Where(currentListing => currentListing.IsPublished)
            .Where(currentListing => currentListing.IsSearchable)
            .Where(currentListing => currentListing.ExpiresAt == null || currentListing.ExpiresAt > now)
            .Where(currentListing => currentListing.PlayerId == null
                || (currentListing.Player != null
                    && currentListing.Player.IsActive
                    && currentListing.Player.IsSearchable))
            .Include(currentListing => currentListing.Player)
                .ThenInclude(player => player!.PlayerSports)
                    .ThenInclude(playerSport => playerSport.Sport)
            .Include(currentListing => currentListing.Sport)
            .SingleOrDefaultAsync(cancellationToken);
        if (listing is null)
        {
            return NotFound();
        }

        var model = await BuildPlayerListingDetailPageModelAsync(listing, currentUser, cancellationToken);
        return View(model);
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ActiveUser)]
    [IgnoreAntiforgeryToken]
    [HttpPost("/player-listings/{listingId:guid}/favorite")]
    public async Task<IActionResult> FavoritePlayerListing(Guid listingId, CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(PlayerListingDetail), new { listingId })
            });
        }

        var listingExists = await QueryVisiblePlayerListingForFavorites(listingId)
            .AnyAsync(cancellationToken);
        if (!listingExists)
        {
            return NotFound();
        }

        var exists = await dbContext.UserFavorites
            .AnyAsync(
                favorite => favorite.UserId == user.Id
                    && favorite.PlayerListingId == listingId,
                cancellationToken);
        if (!exists)
        {
            dbContext.UserFavorites.Add(new UserFavorite
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                PlayerListingId = listingId,
                CreatedAt = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        TempData["StatusMessage"] = "Player listing saved to favorites.";
        return RedirectToAction(nameof(PlayerListingDetail), new { listingId });
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ActiveUser)]
    [IgnoreAntiforgeryToken]
    [HttpPost("/player-listings/{listingId:guid}/favorite/remove")]
    public async Task<IActionResult> RemoveFavoritePlayerListing(
        Guid listingId,
        [FromForm] string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(PlayerListingDetail), new { listingId })
            });
        }

        var favorite = await dbContext.UserFavorites
            .SingleOrDefaultAsync(
                currentFavorite => currentFavorite.UserId == user.Id
                    && currentFavorite.PlayerListingId == listingId,
                cancellationToken);
        if (favorite is not null)
        {
            dbContext.UserFavorites.Remove(favorite);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        TempData["StatusMessage"] = "Player listing removed from favorites.";
        return RedirectToLocalOrOnboarding(returnUrl ?? Url.Action(nameof(PlayerListingDetail), new { listingId }));
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ActiveUser)]
    [HttpGet("/player-listings/{listingId:guid}/report")]
    public async Task<IActionResult> ReportPlayerListing(
        Guid listingId,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = $"/player-listings/{listingId}/report"
            });
        }

        var listing = await QueryVisiblePlayerListingForFavorites(listingId)
            .Include(currentListing => currentListing.Player)
            .SingleOrDefaultAsync(cancellationToken);
        if (listing is null)
        {
            return NotFound();
        }

        var viewerOwnsListing = listing.UserId == user.Id;
        var viewerHasOpenReport = await UserHasOpenPlayerListingReportAsync(
            user.Id,
            listingId,
            cancellationToken);

        var model = BuildPlayerListingReportPageModel(
            listing,
            viewerOwnsListing,
            viewerHasOpenReport);
        return View("ReportListing", model);
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ActiveUser)]
    [HttpPost("/player-listings/{listingId:guid}/report")]
    public async Task<IActionResult> ReportPlayerListing(
        Guid listingId,
        ReportListingRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = $"/player-listings/{listingId}/report"
            });
        }

        var listing = await QueryVisiblePlayerListingForFavorites(listingId)
            .Include(currentListing => currentListing.Player)
            .SingleOrDefaultAsync(cancellationToken);
        if (listing is null)
        {
            return NotFound();
        }

        var viewerOwnsListing = listing.UserId == user.Id;
        var existingOpenReport = await UserHasOpenPlayerListingReportAsync(
            user.Id,
            listingId,
            cancellationToken);
        var reportPageModel = BuildPlayerListingReportPageModel(
            listing,
            viewerOwnsListing,
            existingOpenReport,
            request.Reason,
            request.Details);

        if (viewerOwnsListing)
        {
            return View("ReportListing", reportPageModel);
        }

        var validationMessage = ValidateListingReportRequest(request);
        if (validationMessage is not null)
        {
            ModelState.AddModelError(string.Empty, validationMessage);
            return View("ReportListing", reportPageModel);
        }

        if (existingOpenReport)
        {
            return View("ReportListing", reportPageModel);
        }

        var now = DateTime.UtcNow;
        dbContext.ListingReports.Add(new ListingReport
        {
            Id = Guid.NewGuid(),
            ReporterUserId = user.Id,
            PlayerListingId = listingId,
            Reason = request.Reason.Trim(),
            Details = NormalizeOptional(request.Details),
            Status = TryOutSpotListingReportStatuses.Pending,
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = "Thanks for the report. An administrator will review this listing.";
        return RedirectToAction(nameof(PlayerListingDetail), new { listingId });
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManageTeamProfile)]
    [HttpGet("onboarding/add-team-or-organization")]
    public async Task<IActionResult> AddTeamOrOrganization(CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(AddTeamOrOrganization)) });
        }

        var teamCreationAccess = await ResolveTeamCreationAccessAsync(user.Id, cancellationToken);
        if (!teamCreationAccess.CanCreateAdditionalTeam)
        {
            TempData["StatusMessage"] = teamCreationAccess.BlockReason;
            return RedirectToAction(nameof(ManageTeamOpportunities));
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
            TempData["StatusMessage"] = "Select the Team representative account type before adding a team.";
            return RedirectToAction(nameof(Onboarding));
        }

        return View(model);
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManageTeamProfile)]
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
            TempData["StatusMessage"] = "Select the Team representative account type before adding a team.";
            return RedirectToAction(nameof(Onboarding));
        }

        var teamCreationAccess = await ResolveTeamCreationAccessAsync(user.Id, cancellationToken);
        if (!teamCreationAccess.CanCreateAdditionalTeam)
        {
            TempData["StatusMessage"] = teamCreationAccess.BlockReason;
            return RedirectToAction(nameof(ManageTeamOpportunities));
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
        var uploadedTeamLogo = await ParseUploadedImageAsync(
            model.ProfileImageUpload,
            nameof(model.ProfileImageUpload),
            cancellationToken);
        if (!ModelState.IsValid)
        {
            return View(model);
        }
        var socialMediaLinks = SerializeLinkCollection(
            ("facebook", model.FacebookPageUrl),
            ("x", NormalizeSocialHandleOrUrl(model.XPageUrl, "https://x.com/")),
            ("instagram", NormalizeSocialHandleOrUrl(model.InstagramUrl, "https://instagram.com/")),
            ("youtube", model.YouTubeUrl),
            ("tiktok", NormalizeSocialHandleOrUrl(model.TikTokUrl, "https://tiktok.com/")),
            ("gamechanger_coach", NormalizeOptional(model.GameChangerCoachName)),
            ("gamechanger_team_name", NormalizeOptional(model.GameChangerTeamName)),
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
                IsContactInfoVisible = true,
                IsAcademy = false,
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
            Description = NormalizeOptional(model.TeamDescription),
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
            IsContactInfoVisible = true,
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

        if (uploadedTeamLogo is not null)
        {
            var objectKey = BuildImageObjectKey(TeamLogoDocumentType, teamId, user.Id, uploadedTeamLogo.FileName);
            await imageStorageService.UploadImageAsync(objectKey, uploadedTeamLogo.Content, uploadedTeamLogo.ContentType, cancellationToken);
            team.LogoImageUrl = ToStoredObjectReference(objectKey);
            if (organization is not null)
            {
                organization.LogoImageUrl = ToStoredObjectReference(objectKey);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["StatusMessage"] = "Team setup saved.";
        return RedirectToAction(nameof(Onboarding));
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManageTeamProfile)]
    [HttpGet("onboarding/team-opportunities/{teamId:guid}/edit-profile")]
    public async Task<IActionResult> EditTeamProfile(Guid teamId, CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(EditTeamProfile), new { teamId })
            });
        }

        var managedTeam = await GetManagedTeamRoleContextAsync(user.Id, teamId, cancellationToken);
        if (managedTeam is null)
        {
            TempData["StatusMessage"] = "Team access was not found for this account.";
            return RedirectToAction(nameof(ManageTeamOpportunities));
        }

        var socialLinks = DeserializeLinkCollection(managedTeam.Team.SocialMediaLinks);
        socialLinks.TryGetValue("facebook", out var facebookPageUrl);
        socialLinks.TryGetValue("x", out var xPageUrl);
        socialLinks.TryGetValue("instagram", out var instagramUrl);
        socialLinks.TryGetValue("youtube", out var youtubeUrl);
        socialLinks.TryGetValue("tiktok", out var tikTokUrl);
        socialLinks.TryGetValue("gamechanger_coach", out var gameChangerCoachName);
        socialLinks.TryGetValue("gamechanger_team_name", out var gameChangerTeamName);
        socialLinks.TryGetValue("highlight_video_1", out var highlightVideoUrl1);
        socialLinks.TryGetValue("highlight_video_2", out var highlightVideoUrl2);

        var model = await BuildAddTeamOrOrganizationPageModelAsync(
            user,
            new AddTeamOrOrganizationPageModel
            {
                TeamId = teamId,
                IsEditMode = true,
                TeamName = managedTeam.Team.Name,
                OrganizationName = managedTeam.Team.Organization?.Name,
                TeamRole = managedTeam.Role,
                TeamLevel = managedTeam.Team.TeamLevel,
                TeamDescription = managedTeam.Team.Description,
                GeographicScope = managedTeam.Team.GeographicScope,
                IsSearchable = managedTeam.Team.IsSearchable,
                ContactEmail = managedTeam.Team.Email,
                ContactPhone = managedTeam.Team.PhoneNumber,
                ProfileImageUrl = managedTeam.Team.LogoImageUrl,
                CurrentProfileImageUrl = ResolveTeamLogoPublicUrl(managedTeam.Team.Id, managedTeam.Team.LogoImageUrl),
                HighlightVideoUrl1 = highlightVideoUrl1,
                HighlightVideoUrl2 = highlightVideoUrl2,
                WebsiteUrl = managedTeam.Team.WebsiteUrl,
                City = managedTeam.Team.City,
                State = managedTeam.Team.State,
                ZipCode = managedTeam.Team.ZipCode,
                FacebookPageUrl = facebookPageUrl,
                XPageUrl = xPageUrl,
                InstagramUrl = instagramUrl,
                YouTubeUrl = youtubeUrl,
                TikTokUrl = tikTokUrl,
                GameChangerCoachName = gameChangerCoachName,
                GameChangerTeamName = gameChangerTeamName,
                SelectedSportIds = managedTeam.Team.TeamSports
                    .Where(teamSport => teamSport.IsActive)
                    .Select(teamSport => teamSport.SportId)
                    .Distinct()
                    .ToList()
            },
            cancellationToken);

        return View("AddTeamOrOrganization", model);
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManageTeamProfile)]
    [HttpPost("onboarding/team-opportunities/{teamId:guid}/edit-profile")]
    public async Task<IActionResult> EditTeamProfile(
        Guid teamId,
        AddTeamOrOrganizationPageModel model,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(EditTeamProfile), new { teamId })
            });
        }

        var managedTeam = await GetManagedTeamRoleContextAsync(user.Id, teamId, cancellationToken);
        if (managedTeam is null)
        {
            TempData["StatusMessage"] = "Team access was not found for this account.";
            return RedirectToAction(nameof(ManageTeamOpportunities));
        }

        model.TeamId = teamId;
        model.IsEditMode = true;
        model = await BuildAddTeamOrOrganizationPageModelAsync(user, model, cancellationToken);

        var geographicScope = NormalizeTeamGeographicScope(model.GeographicScope);
        if (geographicScope is null)
        {
            ModelState.AddModelError(nameof(model.GeographicScope), "Choose Local, Regional, or National.");
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
            return View("AddTeamOrOrganization", model);
        }

        var uploadedTeamLogo = await ParseUploadedImageAsync(
            model.ProfileImageUpload,
            nameof(model.ProfileImageUpload),
            cancellationToken);
        var now = DateTime.UtcNow;
        if (!ModelState.IsValid)
        {
            return View("AddTeamOrOrganization", model);
        }

        if (uploadedTeamLogo is not null || model.RemoveProfileImage)
        {
            await ReplaceTeamLogoAsync(
                managedTeam.Team,
                managedTeam.Team.Organization,
                teamId,
                user.Id,
                uploadedTeamLogo,
                model.RemoveProfileImage,
                cancellationToken);
        }

        managedTeam.Team.Name = model.TeamName.Trim();
        managedTeam.Team.TeamLevel = NormalizeOptional(model.TeamLevel);
        managedTeam.Team.Description = NormalizeOptional(model.TeamDescription);
        managedTeam.Team.GeographicScope = geographicScope!;
        if (uploadedTeamLogo is null && !model.RemoveProfileImage)
        {
            managedTeam.Team.LogoImageUrl = NormalizeOptional(model.ProfileImageUrl);
        }
        managedTeam.Team.WebsiteUrl = NormalizeOptional(model.WebsiteUrl);
        managedTeam.Team.City = NormalizeOptional(model.City);
        managedTeam.Team.State = NormalizeState(model.State);
        managedTeam.Team.ZipCode = NormalizeOptional(model.ZipCode);
        managedTeam.Team.PhoneNumber = NormalizeOptional(model.ContactPhone);
        managedTeam.Team.Email = NormalizeOptional(model.ContactEmail);
        managedTeam.Team.IsSearchable = model.IsSearchable;
        managedTeam.Team.SocialMediaLinks = SerializeLinkCollection(
            ("facebook", model.FacebookPageUrl),
            ("x", NormalizeSocialHandleOrUrl(model.XPageUrl, "https://x.com/")),
            ("instagram", NormalizeSocialHandleOrUrl(model.InstagramUrl, "https://instagram.com/")),
            ("youtube", model.YouTubeUrl),
            ("tiktok", NormalizeSocialHandleOrUrl(model.TikTokUrl, "https://tiktok.com/")),
            ("gamechanger_coach", NormalizeOptional(model.GameChangerCoachName)),
            ("gamechanger_team_name", NormalizeOptional(model.GameChangerTeamName)),
            ("highlight_video_1", model.HighlightVideoUrl1),
            ("highlight_video_2", model.HighlightVideoUrl2));
        managedTeam.Team.UpdatedAt = now;

        var activeTeamSports = await dbContext.TeamSports
            .Where(teamSport => teamSport.TeamId == teamId && teamSport.IsActive)
            .ToArrayAsync(cancellationToken);
        var activeSportIdSet = activeTeamSports.Select(teamSport => teamSport.SportId).ToHashSet();

        foreach (var teamSport in activeTeamSports.Where(teamSport => !selectedSports.Contains(teamSport.SportId)))
        {
            teamSport.IsActive = false;
        }

        foreach (var sportId in selectedSports.Where(sportId => !activeSportIdSet.Contains(sportId)))
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
        TempData["StatusMessage"] = "Team profile updated.";
        return RedirectToAction(nameof(ManageTeamOpportunities));
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManageTeamProfile)]
    [HttpGet("onboarding/team-opportunities")]
    public async Task<IActionResult> ManageTeamOpportunities(CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(ManageTeamOpportunities)) });
        }

        var model = await BuildTeamOpportunityDashboardPageModelAsync(user, cancellationToken);
        return View(model);
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManageTeamProfile)]
    [HttpGet("onboarding/team-opportunities/{teamId:guid}")]
    public async Task<IActionResult> TeamOpportunities(
        Guid teamId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(TeamOpportunities), new { teamId, page, pageSize })
            });
        }

        var model = await BuildTeamOpportunityListPageModelAsync(user, teamId, page, pageSize, cancellationToken);
        if (model is null)
        {
            TempData["StatusMessage"] = "Team access was not found for this account.";
            return RedirectToAction(nameof(ManageTeamOpportunities));
        }

        return View(model);
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManageTeamProfile)]
    [HttpGet("onboarding/team-opportunities/{teamId:guid}/new")]
    public async Task<IActionResult> CreateTeamOpportunity(
        Guid teamId,
        [FromQuery] string? type = null,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(CreateTeamOpportunity), new { teamId, type })
            });
        }

        var model = await BuildTeamOpportunityEditorPageModelAsync(user, teamId, null, null, cancellationToken);
        if (model is null)
        {
            TempData["StatusMessage"] = "Team access was not found for this account.";
            return RedirectToAction(nameof(ManageTeamOpportunities));
        }

        if (!model.CanPostOpportunities)
        {
            TempData["StatusMessage"] = "Your current membership does not include opportunity posting.";
            return RedirectToAction(nameof(TeamOpportunities), new { teamId });
        }

        if (TryNormalizeTeamOpportunityType(type, out var normalizedType))
        {
            model.Type = normalizedType;
        }

        return View("EditTeamOpportunity", model);
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManageTeamProfile)]
    [HttpPost("onboarding/team-opportunities/{teamId:guid}/new")]
    public async Task<IActionResult> CreateTeamOpportunity(
        Guid teamId,
        TeamOpportunityEditorPageModel model,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(CreateTeamOpportunity), new { teamId })
            });
        }

        var managedTeam = await GetManagedTeamRoleContextAsync(user.Id, teamId, cancellationToken);
        if (managedTeam is null)
        {
            TempData["StatusMessage"] = "Team access was not found for this account.";
            return RedirectToAction(nameof(ManageTeamOpportunities));
        }

        var postingAccess = await ResolveTeamPostingAccessAsync(user.Id, cancellationToken);
        model = await BuildTeamOpportunityEditorPageModelAsync(
            user,
            teamId,
            null,
            model,
            cancellationToken) ?? model;

        if (!postingAccess.CanPostOpportunities)
        {
            TempData["StatusMessage"] = "Your current membership does not include opportunity posting.";
            return RedirectToAction(nameof(TeamOpportunities), new { teamId });
        }

        var normalizedZipCode = ResolveZipCodeOrAddModelError(
            model.ZipCode,
            managedTeam.Team.ZipCode,
            nameof(model.ZipCode));
        var normalizedRequiredRegistrationFieldCodes = NormalizeOpportunityRegistrationEditorInput(
            model,
            model.CanConfigureTryoutRegistration);
        await ValidateTeamOpportunityEditorInputAsync(
            model,
            managedTeam.Team,
            model.CanConfigureTryoutRegistration,
            normalizedRequiredRegistrationFieldCodes,
            cancellationToken);

        if (model.IsPublished)
        {
            await EnsureCanPublishTeamOpportunityAsync(
                teamId,
                postingAccess,
                currentlyPublished: false,
                cancellationToken);
        }

        if (!ModelState.IsValid)
        {
            return View("EditTeamOpportunity", model);
        }

        var now = DateTime.UtcNow;
        var opportunity = new Opportunity
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            SportId = model.SportId,
            Type = model.Type.Trim(),
            Title = model.Title.Trim(),
            Description = NormalizeOptional(model.Description),
            CompetitionLevel = NormalizeOptional(model.CompetitionLevel),
            AgeGroup = NormalizeOptional(model.AgeGroup),
            RegistrationRequired = model.RegistrationRequired,
            MaxParticipants = model.RegistrationRequired && model.LimitRegistrationCapacity ? model.MaxParticipants : null,
            RegistrationRequiredFieldCodes = model.RegistrationRequired
                ? SerializeRegistrationFieldCodes(normalizedRequiredRegistrationFieldCodes)
                : null,
            WaiverRequired = model.WaiverRequired,
            WaiverMethod = model.WaiverMethod,
            WaiverReturnByEmail = model.WaiverReturnByEmail,
            WaiverReturnInPerson = model.WaiverReturnInPerson,
            RegistrationDeadline = NormalizeUtc(model.RegistrationDeadline),
            RegistrationFee = model.RegistrationFee,
            EventDate = NormalizeUtc(model.EventDate),
            EventEndDate = NormalizeUtc(model.EventEndDate),
            ListingStartDate = NormalizeUtc(model.ListingStartDate),
            ListingEndDate = NormalizeUtc(model.ListingEndDate),
            Location = NormalizeOptional(model.Location),
            Address = NormalizeOptional(model.Address),
            City = NormalizeOptional(model.City) ?? managedTeam.Team.City,
            State = NormalizeState(model.State) ?? managedTeam.Team.State,
            ZipCode = normalizedZipCode,
            ContactEmail = NormalizeOptional(model.ContactEmail) ?? managedTeam.Team.Email,
            ContactPhone = NormalizeOptional(model.ContactPhone) ?? managedTeam.Team.PhoneNumber,
            WebsiteUrl = NormalizeOptional(model.WebsiteUrl) ?? managedTeam.Team.WebsiteUrl,
            PdfUrl = NormalizeOptional(model.PdfUrl),
            RequiredEquipment = NormalizeOptional(model.RequiredEquipment),
            WhatToBring = NormalizeOptional(model.WhatToBring),
            SpecialInstructions = NormalizeOptional(model.SpecialInstructions),
            IsPublished = model.IsPublished,
            PublishedAt = model.IsPublished ? now : null,
            ExpiresAt = NormalizeUtc(model.ListingEndDate) ?? NormalizeUtc(model.ExpiresAt),
            ViewCount = 0,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };

        await ApplyOpportunityFlyerUploadChangesAsync(
            opportunity,
            user.Id,
            model.PdfUpload,
            model.RemoveUploadedPdf,
            nameof(model.PdfUpload),
            cancellationToken);
        await ApplyOpportunityWaiverPdfUploadChangesAsync(
            opportunity,
            user.Id,
            model.WaiverPdfUpload,
            model.RemoveUploadedWaiverPdf,
            nameof(model.WaiverPdfUpload),
            cancellationToken);

        if (!ModelState.IsValid)
        {
            return View("EditTeamOpportunity", model);
        }

        dbContext.Opportunities.Add(opportunity);
        await dbContext.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = "Opportunity created.";
        return RedirectToAction(nameof(TeamOpportunities), new { teamId });
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManageTeamProfile)]
    [HttpGet("onboarding/team-opportunities/{teamId:guid}/{opportunityId:guid}/edit")]
    public async Task<IActionResult> EditTeamOpportunity(
        Guid teamId,
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(EditTeamOpportunity), new { teamId, opportunityId })
            });
        }

        var model = await BuildTeamOpportunityEditorPageModelAsync(
            user,
            teamId,
            opportunityId,
            null,
            cancellationToken);
        if (model is null)
        {
            TempData["StatusMessage"] = "Opportunity access was not found for this account.";
            return RedirectToAction(nameof(TeamOpportunities), new { teamId });
        }

        if (!model.CanPostOpportunities)
        {
            TempData["StatusMessage"] = "Your current membership does not include opportunity posting.";
            return RedirectToAction(nameof(TeamOpportunities), new { teamId });
        }

        return View("EditTeamOpportunity", model);
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManageTeamProfile)]
    [HttpPost("onboarding/team-opportunities/{teamId:guid}/{opportunityId:guid}/edit")]
    public async Task<IActionResult> EditTeamOpportunity(
        Guid teamId,
        Guid opportunityId,
        TeamOpportunityEditorPageModel model,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(EditTeamOpportunity), new { teamId, opportunityId })
            });
        }

        var managedTeam = await GetManagedTeamRoleContextAsync(user.Id, teamId, cancellationToken);
        if (managedTeam is null)
        {
            TempData["StatusMessage"] = "Team access was not found for this account.";
            return RedirectToAction(nameof(ManageTeamOpportunities));
        }

        var postingAccess = await ResolveTeamPostingAccessAsync(user.Id, cancellationToken);
        model = await BuildTeamOpportunityEditorPageModelAsync(
            user,
            teamId,
            opportunityId,
            model,
            cancellationToken) ?? model;

        if (!postingAccess.CanPostOpportunities)
        {
            TempData["StatusMessage"] = "Your current membership does not include opportunity posting.";
            return RedirectToAction(nameof(TeamOpportunities), new { teamId });
        }

        var opportunity = await dbContext.Opportunities
            .SingleOrDefaultAsync(currentOpportunity =>
                currentOpportunity.Id == opportunityId
                && currentOpportunity.TeamId == teamId
                && currentOpportunity.IsActive,
                cancellationToken);
        if (opportunity is null)
        {
            TempData["StatusMessage"] = "Opportunity was not found.";
            return RedirectToAction(nameof(TeamOpportunities), new { teamId });
        }

        var normalizedZipCode = ResolveZipCodeOrAddModelError(
            model.ZipCode,
            managedTeam.Team.ZipCode,
            nameof(model.ZipCode));
        var normalizedRequiredRegistrationFieldCodes = NormalizeOpportunityRegistrationEditorInput(
            model,
            model.CanConfigureTryoutRegistration);
        await ValidateTeamOpportunityEditorInputAsync(
            model,
            managedTeam.Team,
            model.CanConfigureTryoutRegistration,
            normalizedRequiredRegistrationFieldCodes,
            cancellationToken);

        if (model.IsPublished)
        {
            await EnsureCanPublishTeamOpportunityAsync(
                teamId,
                postingAccess,
                opportunity.IsPublished,
                cancellationToken);
        }

        if (!ModelState.IsValid)
        {
            LogTeamOpportunityEditModelStateFailure(
                user.Id,
                teamId,
                opportunityId,
                model,
                "pre-save-validation");
            ViewData["StatusMessage"] = BuildTeamOpportunityEditFailureMessage();
            ViewData["StatusMessageType"] = "error";
            return View("EditTeamOpportunity", model);
        }

        var now = DateTime.UtcNow;
        opportunity.SportId = model.SportId;
        opportunity.Type = model.Type.Trim();
        opportunity.Title = model.Title.Trim();
        opportunity.Description = NormalizeOptional(model.Description);
        opportunity.CompetitionLevel = NormalizeOptional(model.CompetitionLevel);
        opportunity.AgeGroup = NormalizeOptional(model.AgeGroup);
        opportunity.RegistrationRequired = model.RegistrationRequired;
        opportunity.MaxParticipants = model.RegistrationRequired && model.LimitRegistrationCapacity ? model.MaxParticipants : null;
        opportunity.RegistrationRequiredFieldCodes = model.RegistrationRequired
            ? SerializeRegistrationFieldCodes(normalizedRequiredRegistrationFieldCodes)
            : null;
        opportunity.WaiverRequired = model.WaiverRequired;
        opportunity.WaiverMethod = model.WaiverMethod;
        opportunity.WaiverReturnByEmail = model.WaiverReturnByEmail;
        opportunity.WaiverReturnInPerson = model.WaiverReturnInPerson;
        opportunity.RegistrationDeadline = NormalizeUtc(model.RegistrationDeadline);
        opportunity.RegistrationFee = model.RegistrationFee;
        opportunity.EventDate = NormalizeUtc(model.EventDate);
        opportunity.EventEndDate = NormalizeUtc(model.EventEndDate);
        opportunity.ListingStartDate = NormalizeUtc(model.ListingStartDate);
        opportunity.ListingEndDate = NormalizeUtc(model.ListingEndDate);
        opportunity.Location = NormalizeOptional(model.Location);
        opportunity.Address = NormalizeOptional(model.Address);
        opportunity.City = NormalizeOptional(model.City) ?? managedTeam.Team.City;
        opportunity.State = NormalizeState(model.State) ?? managedTeam.Team.State;
        opportunity.ZipCode = normalizedZipCode;
        opportunity.ContactEmail = NormalizeOptional(model.ContactEmail) ?? managedTeam.Team.Email;
        opportunity.ContactPhone = NormalizeOptional(model.ContactPhone) ?? managedTeam.Team.PhoneNumber;
        opportunity.WebsiteUrl = NormalizeOptional(model.WebsiteUrl) ?? managedTeam.Team.WebsiteUrl;
        opportunity.PdfUrl = NormalizeOptional(model.PdfUrl);
        opportunity.RequiredEquipment = NormalizeOptional(model.RequiredEquipment);
        opportunity.WhatToBring = NormalizeOptional(model.WhatToBring);
        opportunity.SpecialInstructions = NormalizeOptional(model.SpecialInstructions);
        opportunity.IsPublished = model.IsPublished;
        opportunity.PublishedAt = model.IsPublished
            ? opportunity.PublishedAt ?? now
            : null;
        opportunity.ExpiresAt = NormalizeUtc(model.ListingEndDate) ?? NormalizeUtc(model.ExpiresAt);
        opportunity.UpdatedAt = now;

        await ApplyOpportunityFlyerUploadChangesAsync(
            opportunity,
            user.Id,
            model.PdfUpload,
            model.RemoveUploadedPdf,
            nameof(model.PdfUpload),
            cancellationToken);
        await ApplyOpportunityWaiverPdfUploadChangesAsync(
            opportunity,
            user.Id,
            model.WaiverPdfUpload,
            model.RemoveUploadedWaiverPdf,
            nameof(model.WaiverPdfUpload),
            cancellationToken);

        if (!ModelState.IsValid)
        {
            LogTeamOpportunityEditModelStateFailure(
                user.Id,
                teamId,
                opportunityId,
                model,
                "post-upload-validation");
            ViewData["StatusMessage"] = BuildTeamOpportunityEditFailureMessage();
            ViewData["StatusMessageType"] = "error";
            return View("EditTeamOpportunity", model);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Team opportunity edit saved. UserId {UserId}, TeamId {TeamId}, OpportunityId {OpportunityId}, IsPublished {IsPublished}.",
            user.Id,
            teamId,
            opportunityId,
            model.IsPublished);

        TempData["StatusMessage"] = "Opportunity updated.";
        return RedirectToAction(nameof(TeamOpportunities), new { teamId });
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManageTeamProfile)]
    [HttpPost("onboarding/team-opportunities/{teamId:guid}/{opportunityId:guid}/publication")]
    public async Task<IActionResult> SetTeamOpportunityPublication(
        Guid teamId,
        Guid opportunityId,
        [FromForm] bool isPublished,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(TeamOpportunities), new { teamId })
            });
        }

        var managedTeam = await GetManagedTeamRoleContextAsync(user.Id, teamId, cancellationToken);
        if (managedTeam is null)
        {
            TempData["StatusMessage"] = "Team access was not found for this account.";
            return RedirectToAction(nameof(ManageTeamOpportunities));
        }

        var opportunity = await dbContext.Opportunities
            .SingleOrDefaultAsync(currentOpportunity =>
                currentOpportunity.Id == opportunityId
                && currentOpportunity.TeamId == teamId
                && currentOpportunity.IsActive,
                cancellationToken);
        if (opportunity is null)
        {
            TempData["StatusMessage"] = "Opportunity was not found.";
            return RedirectToAction(nameof(TeamOpportunities), new { teamId });
        }

        if (isPublished)
        {
            var postingAccess = await ResolveTeamPostingAccessAsync(user.Id, cancellationToken);
            if (!postingAccess.CanPostOpportunities)
            {
                TempData["StatusMessage"] = "Your current membership does not include opportunity posting.";
                return RedirectToAction(nameof(TeamOpportunities), new { teamId });
            }

            await EnsureCanPublishTeamOpportunityAsync(
                teamId,
                postingAccess,
                opportunity.IsPublished,
                cancellationToken);
            if (!ModelState.IsValid)
            {
                var publicationError = ModelState.Values
                    .SelectMany(entry => entry.Errors)
                    .Select(error => error.ErrorMessage)
                    .FirstOrDefault();
                TempData["StatusMessage"] = string.IsNullOrWhiteSpace(publicationError)
                    ? "Publishing could not be completed."
                    : publicationError;
                return RedirectToAction(nameof(TeamOpportunities), new { teamId });
            }
        }

        var now = DateTime.UtcNow;
        opportunity.IsPublished = isPublished;
        opportunity.PublishedAt = isPublished
            ? opportunity.PublishedAt ?? now
            : opportunity.PublishedAt;
        opportunity.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = isPublished
            ? "Opportunity published."
            : "Opportunity unpublished.";
        return RedirectToAction(nameof(TeamOpportunities), new { teamId });
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManageTeamProfile)]
    [HttpPost("onboarding/team-opportunities/{teamId:guid}/{opportunityId:guid}/deactivate")]
    public async Task<IActionResult> DeactivateTeamOpportunity(
        Guid teamId,
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(TeamOpportunities), new { teamId })
            });
        }

        var managedTeam = await GetManagedTeamRoleContextAsync(user.Id, teamId, cancellationToken);
        if (managedTeam is null)
        {
            TempData["StatusMessage"] = "Team access was not found for this account.";
            return RedirectToAction(nameof(ManageTeamOpportunities));
        }

        var opportunity = await dbContext.Opportunities
            .SingleOrDefaultAsync(currentOpportunity =>
                currentOpportunity.Id == opportunityId
                && currentOpportunity.TeamId == teamId
                && currentOpportunity.IsActive,
                cancellationToken);
        if (opportunity is null)
        {
            TempData["StatusMessage"] = "Opportunity was not found.";
            return RedirectToAction(nameof(TeamOpportunities), new { teamId });
        }

        opportunity.IsActive = false;
        opportunity.IsPublished = false;
        opportunity.UpdatedAt = DateTime.UtcNow;

        await RemoveOpportunityPdfAsync(opportunity, cancellationToken);
        await RemoveOpportunityWaiverPdfAsync(opportunity, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = "Opportunity deactivated.";
        return RedirectToAction(nameof(TeamOpportunities), new { teamId });
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManageTeamProfile)]
    [HttpPost("onboarding/team-opportunities/{teamId:guid}/{opportunityId:guid}/registrations/{registrationId:guid}/attendance")]
    public async Task<IActionResult> SetTeamOpportunityRegistrationAttendance(
        Guid teamId,
        Guid opportunityId,
        Guid registrationId,
        [FromForm] bool isPresent,
        [FromForm] int? page = null,
        [FromForm] int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(TeamOpportunities), new { teamId, page, pageSize })
            });
        }

        var managedTeam = await GetManagedTeamRoleContextAsync(user.Id, teamId, cancellationToken);
        if (managedTeam is null)
        {
            TempData["StatusMessage"] = "Team access was not found for this account.";
            return RedirectToAction(nameof(ManageTeamOpportunities));
        }

        var registration = await dbContext.Registrations
            .SingleOrDefaultAsync(current =>
                current.Id == registrationId
                && current.OpportunityId == opportunityId
                && current.Opportunity.TeamId == teamId
                && current.Opportunity.IsActive,
                cancellationToken);
        if (registration is null)
        {
            TempData["StatusMessage"] = "Registration was not found for this listing.";
            return RedirectToAction(nameof(TeamOpportunities), new { teamId, page, pageSize });
        }

        var now = DateTime.UtcNow;
        if (isPresent)
        {
            registration.AttendanceStatus = RegistrationAttendancePresentStatus;
            registration.CheckInTime ??= now;
        }
        else
        {
            registration.AttendanceStatus = RegistrationAttendancePendingStatus;
            registration.CheckInTime = null;
        }

        registration.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = isPresent
            ? "Player marked present."
            : "Player attendance reset.";
        return RedirectToAction(nameof(TeamOpportunities), new { teamId, page, pageSize });
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManageTeamProfile)]
    [HttpPost("onboarding/team-opportunities/{teamId:guid}/{opportunityId:guid}/registrations/{registrationId:guid}/waiver")]
    public async Task<IActionResult> SetTeamOpportunityRegistrationWaiverStatus(
        Guid teamId,
        Guid opportunityId,
        Guid registrationId,
        [FromForm] bool waiverReceived,
        [FromForm] int? page = null,
        [FromForm] int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(TeamOpportunities), new { teamId, page, pageSize })
            });
        }

        var managedTeam = await GetManagedTeamRoleContextAsync(user.Id, teamId, cancellationToken);
        if (managedTeam is null)
        {
            TempData["StatusMessage"] = "Team access was not found for this account.";
            return RedirectToAction(nameof(ManageTeamOpportunities));
        }

        var registration = await dbContext.Registrations
            .SingleOrDefaultAsync(current =>
                current.Id == registrationId
                && current.OpportunityId == opportunityId
                && current.Opportunity.TeamId == teamId
                && current.Opportunity.IsActive,
                cancellationToken);
        if (registration is null)
        {
            TempData["StatusMessage"] = "Registration was not found for this listing.";
            return RedirectToAction(nameof(TeamOpportunities), new { teamId, page, pageSize });
        }

        var now = DateTime.UtcNow;
        registration.WaiverSigned = waiverReceived;
        registration.WaiverSignedAt = waiverReceived ? registration.WaiverSignedAt ?? now : null;
        if (!waiverReceived)
        {
            registration.WaiverSignerName = null;
        }

        registration.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = waiverReceived
            ? "Waiver marked received."
            : "Waiver status reset.";
        return RedirectToAction(nameof(TeamOpportunities), new { teamId, page, pageSize });
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManageTeamProfile)]
    [HttpPost("onboarding/team-opportunities/{teamId:guid}/{opportunityId:guid}/registrations/{registrationId:guid}/status")]
    public async Task<IActionResult> SetTeamOpportunityRegistrationStatus(
        Guid teamId,
        Guid opportunityId,
        Guid registrationId,
        [FromForm] string status = "",
        [FromForm] int? page = null,
        [FromForm] int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(TeamOpportunities), new { teamId, page, pageSize })
            });
        }

        var managedTeam = await GetManagedTeamRoleContextAsync(user.Id, teamId, cancellationToken);
        if (managedTeam is null)
        {
            TempData["StatusMessage"] = "Team access was not found for this account.";
            return RedirectToAction(nameof(ManageTeamOpportunities));
        }

        var normalizedStatus = NormalizeRegistrationWorkflowStatus(status);
        if (normalizedStatus is null)
        {
            TempData["StatusMessage"] = "Registration status update was not recognized.";
            return RedirectToAction(nameof(TeamOpportunities), new { teamId, page, pageSize });
        }

        var registration = await dbContext.Registrations
            .SingleOrDefaultAsync(current =>
                current.Id == registrationId
                && current.OpportunityId == opportunityId
                && current.Opportunity.TeamId == teamId
                && current.Opportunity.IsActive,
                cancellationToken);
        if (registration is null)
        {
            TempData["StatusMessage"] = "Registration was not found for this listing.";
            return RedirectToAction(nameof(TeamOpportunities), new { teamId, page, pageSize });
        }

        registration.Status = normalizedStatus;
        registration.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = normalizedStatus switch
        {
            RegistrationWorkflowStatusAccepted => "Registration accepted.",
            RegistrationWorkflowStatusDeclined => "Registration declined.",
            _ => "Registration moved back to pending review."
        };

        return RedirectToAction(nameof(TeamOpportunities), new { teamId, page, pageSize });
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManageTeamProfile)]
    [HttpGet("onboarding/team-opportunities/{teamId:guid}/{opportunityId:guid}/registrations/share")]
    public IActionResult ShareTeamOpportunityRegistrations(
        Guid teamId,
        Guid opportunityId)
    {
        return RedirectToAction(nameof(CheckInTeamOpportunityRegistrations), new { teamId, opportunityId });
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManageTeamProfile)]
    [HttpGet("onboarding/team-opportunities/{teamId:guid}/{opportunityId:guid}/registrations/check-in")]
    public async Task<IActionResult> CheckInTeamOpportunityRegistrations(
        Guid teamId,
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(CheckInTeamOpportunityRegistrations), new { teamId, opportunityId })
            });
        }

        var model = await BuildTeamOpportunityRegistrationSharePageModelAsync(
            user,
            teamId,
            opportunityId,
            cancellationToken);
        if (model is null)
        {
            TempData["StatusMessage"] = "Registration roster was not found for this team.";
            return RedirectToAction(nameof(TeamOpportunities), new { teamId });
        }

        return View(model);
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManageTeamProfile)]
    [HttpGet("onboarding/team-opportunities/{teamId:guid}/{opportunityId:guid}/registrations/evaluation")]
    public async Task<IActionResult> EvaluateTeamOpportunityRegistrations(
        Guid teamId,
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(EvaluateTeamOpportunityRegistrations), new { teamId, opportunityId })
            });
        }

        var model = await BuildTeamOpportunityRegistrationSharePageModelAsync(
            user,
            teamId,
            opportunityId,
            cancellationToken);
        if (model is null)
        {
            TempData["StatusMessage"] = "Registration roster was not found for this team.";
            return RedirectToAction(nameof(TeamOpportunities), new { teamId });
        }

        return View(model);
    }

    [AllowAnonymous]
    [HttpGet("/opportunities/{opportunityId:guid}")]
    public async Task<IActionResult> TeamOpportunityDetail(
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await GetCurrentWebUserAsync();
        var hasAdvancedOpportunitySearch = currentUser is not null
            && await HasAdvancedOpportunitySearchAsync(currentUser.Id, cancellationToken);
        var opportunity = await GetPublishedOpportunityForDiscoveryAsync(
            opportunityId,
            hasAdvancedOpportunitySearch,
            cancellationToken);
        if (opportunity is null)
        {
            return NotFound();
        }

        var model = await BuildTeamOpportunityDetailPageModelAsync(
            opportunity,
            currentUser,
            null,
            cancellationToken);
        return View(model);
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ActiveUser)]
    [IgnoreAntiforgeryToken]
    [HttpPost("/opportunities/{opportunityId:guid}/favorite")]
    public async Task<IActionResult> FavoriteTeamOpportunity(
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(TeamOpportunityDetail), new { opportunityId })
            });
        }

        var hasAdvancedOpportunitySearch = await HasAdvancedOpportunitySearchAsync(user.Id, cancellationToken);
        var opportunity = await GetPublishedOpportunityForDiscoveryAsync(
            opportunityId,
            hasAdvancedOpportunitySearch,
            cancellationToken);
        if (opportunity is null)
        {
            return NotFound();
        }

        var exists = await dbContext.UserFavorites
            .AnyAsync(
                favorite => favorite.UserId == user.Id
                    && favorite.OpportunityId == opportunityId,
                cancellationToken);
        if (!exists)
        {
            dbContext.UserFavorites.Add(new UserFavorite
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                OpportunityId = opportunityId,
                CreatedAt = DateTime.UtcNow
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        TempData["StatusMessage"] = "Opportunity saved to favorites.";
        return RedirectToAction(nameof(TeamOpportunityDetail), new { opportunityId });
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ActiveUser)]
    [IgnoreAntiforgeryToken]
    [HttpPost("/opportunities/{opportunityId:guid}/favorite/remove")]
    public async Task<IActionResult> RemoveFavoriteTeamOpportunity(
        Guid opportunityId,
        [FromForm] string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(TeamOpportunityDetail), new { opportunityId })
            });
        }

        var favorite = await dbContext.UserFavorites
            .SingleOrDefaultAsync(
                currentFavorite => currentFavorite.UserId == user.Id
                    && currentFavorite.OpportunityId == opportunityId,
                cancellationToken);
        if (favorite is not null)
        {
            dbContext.UserFavorites.Remove(favorite);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        TempData["StatusMessage"] = "Opportunity removed from favorites.";
        return RedirectToLocalOrOnboarding(returnUrl ?? Url.Action(nameof(TeamOpportunityDetail), new { opportunityId }));
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ActiveUser)]
    [HttpGet("/opportunities/{opportunityId:guid}/report")]
    public async Task<IActionResult> ReportTeamOpportunity(
        Guid opportunityId,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = $"/opportunities/{opportunityId}/report"
            });
        }

        var hasAdvancedOpportunitySearch = await HasAdvancedOpportunitySearchAsync(user.Id, cancellationToken);
        var opportunity = await GetPublishedOpportunityForDiscoveryAsync(
            opportunityId,
            hasAdvancedOpportunitySearch,
            cancellationToken);
        if (opportunity is null)
        {
            return NotFound();
        }

        var viewerManagesTeam = await UserManagesTeamAsync(user.Id, opportunity.TeamId, cancellationToken);
        var viewerHasOpenReport = await UserHasOpenOpportunityReportAsync(
            user.Id,
            opportunityId,
            cancellationToken);

        var model = BuildTeamOpportunityReportPageModel(
            opportunity,
            viewerManagesTeam,
            viewerHasOpenReport);
        return View("ReportListing", model);
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ActiveUser)]
    [HttpPost("/opportunities/{opportunityId:guid}/report")]
    public async Task<IActionResult> ReportTeamOpportunity(
        Guid opportunityId,
        ReportListingRequest request,
        CancellationToken cancellationToken = default)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = $"/opportunities/{opportunityId}/report"
            });
        }

        var hasAdvancedOpportunitySearch = await HasAdvancedOpportunitySearchAsync(user.Id, cancellationToken);
        var opportunity = await GetPublishedOpportunityForDiscoveryAsync(
            opportunityId,
            hasAdvancedOpportunitySearch,
            cancellationToken);
        if (opportunity is null)
        {
            return NotFound();
        }

        var viewerManagesTeam = await UserManagesTeamAsync(user.Id, opportunity.TeamId, cancellationToken);
        var existingOpenReport = await UserHasOpenOpportunityReportAsync(
            user.Id,
            opportunityId,
            cancellationToken);
        var reportPageModel = BuildTeamOpportunityReportPageModel(
            opportunity,
            viewerManagesTeam,
            existingOpenReport,
            request.Reason,
            request.Details);
        if (viewerManagesTeam)
        {
            return View("ReportListing", reportPageModel);
        }

        var validationMessage = ValidateListingReportRequest(request);
        if (validationMessage is not null)
        {
            ModelState.AddModelError(string.Empty, validationMessage);
            return View("ReportListing", reportPageModel);
        }

        if (existingOpenReport)
        {
            return View("ReportListing", reportPageModel);
        }

        var now = DateTime.UtcNow;
        dbContext.ListingReports.Add(new ListingReport
        {
            Id = Guid.NewGuid(),
            ReporterUserId = user.Id,
            OpportunityId = opportunityId,
            Reason = request.Reason.Trim(),
            Details = NormalizeOptional(request.Details),
            Status = TryOutSpotListingReportStatuses.Pending,
            CreatedAt = now,
            UpdatedAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        TempData["StatusMessage"] = "Thanks for the report. An administrator will review this listing.";
        return RedirectToAction(nameof(TeamOpportunityDetail), new { opportunityId });
    }

    [Authorize(
        AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie,
        Policy = TryOutSpotAuthorizationPolicies.ManagePlayerProfile)]
    [IgnoreAntiforgeryToken]
    [HttpPost("/opportunities/{opportunityId:guid}/register")]
    public async Task<IActionResult> RegisterForTeamOpportunity(
        Guid opportunityId,
        [Bind(Prefix = "RegistrationForm")]
        TeamOpportunityRegistrationInputPageModel registrationForm,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Team opportunity registration submit started. OpportunityId {OpportunityId}, PlayerId {PlayerId}",
            opportunityId,
            registrationForm.PlayerId);

        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            logger.LogWarning(
                "Team opportunity registration submit rejected because user was not authenticated. OpportunityId {OpportunityId}",
                opportunityId);
            return RedirectToAction(nameof(Login), new
            {
                returnUrl = Url.Action(nameof(TeamOpportunityDetail), new { opportunityId })
            });
        }

        var hasAdvancedOpportunitySearch = await HasAdvancedOpportunitySearchAsync(user.Id, cancellationToken);
        var opportunity = await GetPublishedOpportunityForDiscoveryAsync(
            opportunityId,
            hasAdvancedOpportunitySearch,
            cancellationToken);
        if (opportunity is null)
        {
            return NotFound();
        }

        var requiredRegistrationFieldCodes = opportunity.RegistrationRequired
            ? DeserializeRegistrationFieldCodes(opportunity.RegistrationRequiredFieldCodes)
            : [];
        var now = DateTime.UtcNow;
        var activeRegistrationCount = await GetActiveOpportunityRegistrationCountAsync(
            opportunity.Id,
            cancellationToken);
        var registrationClosedReason = GetRegistrationClosedReason(
            opportunity,
            now,
            activeRegistrationCount);
        if (!opportunity.RegistrationRequired || registrationClosedReason is not null)
        {
            TempData["StatusMessage"] = registrationClosedReason
                ?? "Registration is not required for this opportunity.";
            return RedirectToAction(nameof(TeamOpportunityDetail), new { opportunityId });
        }

        var managedPlayerOptions = await GetOpportunityRegistrationPlayersAsync(
            opportunity.Id,
            user.Id,
            cancellationToken);
        var selectedPlayer = managedPlayerOptions.FirstOrDefault(
            playerOption => playerOption.PlayerId == registrationForm.PlayerId);
        if (registrationForm.PlayerId == Guid.Empty)
        {
            ModelState.AddModelError(
                BuildRegistrationFormModelStateKey(nameof(registrationForm.PlayerId)),
                "Choose a player profile to register.");
        }
        else if (selectedPlayer is null)
        {
            ModelState.AddModelError(
                BuildRegistrationFormModelStateKey(nameof(registrationForm.PlayerId)),
                "Choose a player profile you can manage.");
        }
        else if (selectedPlayer.AlreadyRegistered)
        {
            ModelState.AddModelError(
                BuildRegistrationFormModelStateKey(nameof(registrationForm.PlayerId)),
                "This player is already registered for this tryout.");
        }

        ApplySelectedPlayerRegistrationDefaults(registrationForm, selectedPlayer);
        ValidateOpportunityRegistrationForm(registrationForm, requiredRegistrationFieldCodes);

        if (!ModelState.IsValid)
        {
            logger.LogWarning(
                "Team opportunity registration submit failed validation. OpportunityId {OpportunityId}, UserId {UserId}, PlayerId {PlayerId}, ModelStateErrors {@ModelStateErrors}",
                opportunityId,
                user.Id,
                registrationForm.PlayerId,
                BuildModelStateErrorMap());
            var invalidModel = await BuildTeamOpportunityDetailPageModelAsync(
                opportunity,
                user,
                registrationForm,
                cancellationToken);
            return View("TeamOpportunityDetail", invalidModel);
        }

        var normalizedPlayerName = NormalizeOptional(registrationForm.PlayerName);
        var normalizedPlayerBirthDate = NormalizeUtc(registrationForm.PlayerBirthDate);
        var normalizedPlayerSchool = NormalizeOptional(registrationForm.PlayerSchool);
        var normalizedPlayerPhone = NormalizeOptional(registrationForm.PlayerPhone);
        var normalizedPlayerEmail = NormalizeOptional(registrationForm.PlayerEmail);
        var normalizedGuardianName = NormalizeOptional(registrationForm.GuardianName);
        var normalizedGuardianEmail = NormalizeOptional(registrationForm.GuardianEmail);
        var normalizedGuardianPhone = NormalizeOptional(registrationForm.GuardianPhone);
        var normalizedEmergencyContactName = NormalizeOptional(registrationForm.EmergencyContactName);
        var normalizedEmergencyContactPhone = NormalizeOptional(registrationForm.EmergencyContactPhone);
        var normalizedMedicalInfo = NormalizeOptional(registrationForm.MedicalInfo);
        var normalizedWaiverSignerName = NormalizeOptional(registrationForm.WaiverSignerName);
        var normalizedAdditionalNotes = NormalizeOptional(registrationForm.AdditionalNotes);

        var registrationData = BuildOpportunityRegistrationDataJson(
            requiredRegistrationFieldCodes,
            normalizedPlayerName,
            normalizedPlayerBirthDate,
            normalizedPlayerSchool,
            normalizedPlayerPhone,
            normalizedPlayerEmail,
            normalizedGuardianName,
            normalizedGuardianEmail,
            normalizedGuardianPhone,
            normalizedAdditionalNotes,
            now);

        var registration = new Registration
        {
            Id = Guid.NewGuid(),
            OpportunityId = opportunity.Id,
            PlayerId = registrationForm.PlayerId,
            RegisteredByUserId = user.Id,
            Status = RegistrationWorkflowStatusPending,
            RegistrationData = registrationData,
            PaymentStatus = opportunity.RegistrationFee > 0 ? "in_person" : "not_required",
            Amount = opportunity.RegistrationFee > 0 ? opportunity.RegistrationFee : null,
            MedicalInfo = normalizedMedicalInfo,
            EmergencyContactName = normalizedEmergencyContactName,
            EmergencyContactPhone = normalizedEmergencyContactPhone,
            WaiverSigned = registrationForm.WaiverAcknowledged,
            WaiverSignedAt = registrationForm.WaiverAcknowledged ? now : null,
            WaiverSignerName = registrationForm.WaiverAcknowledged ? normalizedWaiverSignerName : null,
            AttendanceStatus = RegistrationAttendancePendingStatus,
            Notes = normalizedAdditionalNotes,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Registrations.Add(registration);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Team opportunity registration saved. OpportunityId {OpportunityId}, UserId {UserId}, PlayerId {PlayerId}, RegistrationId {RegistrationId}",
            opportunityId,
            user.Id,
            registrationForm.PlayerId,
            registration.Id);

        TempData["StatusMessage"] = "Registration submitted. The team will review your entry.";
        return RedirectToAction(nameof(TeamOpportunityDetail), new { opportunityId });
    }

    private async Task<Opportunity?> GetPublishedOpportunityForDiscoveryAsync(
        Guid opportunityId,
        bool hasAdvancedOpportunitySearch,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        IQueryable<Opportunity> query = dbContext.Opportunities
            .AsNoTracking()
            .Where(currentOpportunity => currentOpportunity.Id == opportunityId)
            .Where(currentOpportunity => currentOpportunity.IsActive)
            .Where(currentOpportunity => currentOpportunity.IsPublished)
            .Where(currentOpportunity =>
                (currentOpportunity.ListingEndDate ?? currentOpportunity.ExpiresAt) == null
                || (currentOpportunity.ListingEndDate ?? currentOpportunity.ExpiresAt) > now)
            .Where(currentOpportunity => currentOpportunity.Team.IsActive)
            .Where(currentOpportunity => currentOpportunity.Team.IsSearchable);

        if (!hasAdvancedOpportunitySearch)
        {
            query = query.Where(currentOpportunity =>
                currentOpportunity.ListingStartDate == null
                || currentOpportunity.ListingStartDate <= now);
        }

        return await query
            .Include(currentOpportunity => currentOpportunity.Team)
                .ThenInclude(team => team.Organization)
            .Include(currentOpportunity => currentOpportunity.Sport)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private IQueryable<PlayerListing> QueryVisiblePlayerListingForFavorites(Guid listingId)
    {
        var now = DateTime.UtcNow;
        return dbContext.PlayerListings
            .AsNoTracking()
            .Where(currentListing => currentListing.Id == listingId)
            .Where(currentListing => currentListing.IsActive)
            .Where(currentListing => currentListing.IsPublished)
            .Where(currentListing => currentListing.IsSearchable)
            .Where(currentListing => currentListing.ExpiresAt == null || currentListing.ExpiresAt > now)
            .Where(currentListing => currentListing.PlayerId == null
                || (currentListing.Player != null
                    && currentListing.Player.IsActive
                    && currentListing.Player.IsSearchable));
    }

    private static ListingReportPageModel BuildPlayerListingReportPageModel(
        PlayerListing listing,
        bool viewerOwnsListing,
        bool viewerHasOpenReport,
        string? reason = null,
        string? details = null)
    {
        var playerName = listing.Player is null
            ? null
            : $"{listing.Player.FirstName} {listing.Player.LastName}".Trim();
        var subtitle = string.IsNullOrWhiteSpace(playerName)
            ? GetPlayerListingTypeLabel(listing.ListingType)
            : $"Player: {playerName}";
        var blockMessage = viewerOwnsListing
            ? "You cannot report your own listing."
            : viewerHasOpenReport
                ? "This listing is already in review from your report."
                : null;

        return new ListingReportPageModel
        {
            TargetTypeLabel = "Player listing",
            Title = listing.Title,
            Subtitle = subtitle,
            ReturnUrl = $"/player-listings/{listing.Id}",
            PostUrl = $"/player-listings/{listing.Id}/report",
            ViewerCanReport = !viewerOwnsListing && !viewerHasOpenReport,
            BlockMessage = blockMessage,
            Reason = reason,
            Details = details,
            ReasonOptions = ListingReportReasonOptions
        };
    }

    private static ListingReportPageModel BuildTeamOpportunityReportPageModel(
        Opportunity opportunity,
        bool viewerManagesTeam,
        bool viewerHasOpenReport,
        string? reason = null,
        string? details = null)
    {
        var subtitleParts = new List<string>
        {
            GetOpportunityTypeLabel(opportunity.Type)
        };
        if (!string.IsNullOrWhiteSpace(opportunity.Sport?.Name))
        {
            subtitleParts.Add(opportunity.Sport.Name);
        }

        if (!string.IsNullOrWhiteSpace(opportunity.Team?.Name))
        {
            subtitleParts.Add(opportunity.Team.Name);
        }

        var blockMessage = viewerManagesTeam
            ? "You cannot report an opportunity for a team you manage."
            : viewerHasOpenReport
                ? "This listing is already in review from your report."
                : null;

        return new ListingReportPageModel
        {
            TargetTypeLabel = "Team opportunity",
            Title = opportunity.Title,
            Subtitle = string.Join(" - ", subtitleParts),
            ReturnUrl = $"/opportunities/{opportunity.Id}",
            PostUrl = $"/opportunities/{opportunity.Id}/report",
            ViewerCanReport = !viewerManagesTeam && !viewerHasOpenReport,
            BlockMessage = blockMessage,
            Reason = reason,
            Details = details,
            ReasonOptions = ListingReportReasonOptions
        };
    }

    private Task<bool> UserHasOpenPlayerListingReportAsync(
        Guid userId,
        Guid listingId,
        CancellationToken cancellationToken)
    {
        return dbContext.ListingReports
            .AsNoTracking()
            .Where(report => report.ReporterUserId == userId)
            .Where(report => report.PlayerListingId == listingId)
            .AnyAsync(report => report.Status == TryOutSpotListingReportStatuses.Pending
                || report.Status == TryOutSpotListingReportStatuses.InReview, cancellationToken);
    }

    private Task<bool> UserHasOpenOpportunityReportAsync(
        Guid userId,
        Guid opportunityId,
        CancellationToken cancellationToken)
    {
        return dbContext.ListingReports
            .AsNoTracking()
            .Where(report => report.ReporterUserId == userId)
            .Where(report => report.OpportunityId == opportunityId)
            .AnyAsync(report => report.Status == TryOutSpotListingReportStatuses.Pending
                || report.Status == TryOutSpotListingReportStatuses.InReview, cancellationToken);
    }

    private Task<bool> UserManagesTeamAsync(
        Guid userId,
        Guid teamId,
        CancellationToken cancellationToken)
    {
        return dbContext.UserTeamRoles
            .AsNoTracking()
            .AnyAsync(teamRole => teamRole.UserId == userId
                && teamRole.TeamId == teamId
                && teamRole.IsActive, cancellationToken);
    }

    private static string? ValidateListingReportRequest(ReportListingRequest request)
    {
        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            return "Choose a reason before submitting a report.";
        }

        if (reason.Length > 100)
        {
            return "Report reason must be 100 characters or fewer.";
        }

        if (!string.IsNullOrWhiteSpace(request.Details) && request.Details.Trim().Length > 2000)
        {
            return "Report details must be 2,000 characters or fewer.";
        }

        return null;
    }

    private async Task<int> GetActiveOpportunityRegistrationCountAsync(
        Guid opportunityId,
        CancellationToken cancellationToken)
    {
        var registrationStatuses = await dbContext.Registrations
            .AsNoTracking()
            .Where(registration => registration.OpportunityId == opportunityId)
            .Select(registration => registration.Status)
            .ToArrayAsync(cancellationToken);

        return registrationStatuses.Count(status =>
            ClassifyRegistrationStatus(status) != RegistrationStatusCategory.Declined);
    }

    private static string? GetRegistrationClosedReason(
        Opportunity opportunity,
        DateTime now,
        int activeRegistrationCount)
    {
        if (opportunity.RegistrationDeadline.HasValue
            && opportunity.RegistrationDeadline.Value < now)
        {
            return "Registration is closed because the deadline has passed.";
        }

        if (opportunity.MaxParticipants is > 0
            && activeRegistrationCount >= opportunity.MaxParticipants.Value)
        {
            return "Registration is closed because this session reached max capacity.";
        }

        return null;
    }

    private async Task<OpportunityRegistrationPlayerOptionPageItem[]> GetOpportunityRegistrationPlayersAsync(
        Guid opportunityId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var managedPlayers = await dbContext.UserPlayerRelationships
            .AsNoTracking()
            .Where(relationship => relationship.UserId == userId)
            .Where(relationship => relationship.CanManage)
            .Where(relationship => relationship.Player.IsActive)
            .Select(relationship => relationship.Player)
            .Distinct()
            .OrderBy(player => player.LastName)
            .ThenBy(player => player.FirstName)
            .Select(player => new
            {
                player.Id,
                DisplayName = (player.FirstName + " " + player.LastName).Trim(),
                ProfileName = (player.FirstName + " " + player.LastName).Trim(),
                ProfileBirthDate = (DateTime?)player.DateOfBirth.Date,
                player.SchoolName,
                player.ContactPhone,
                player.ContactEmail
            })
            .ToArrayAsync(cancellationToken);

        if (managedPlayers.Length == 0)
        {
            return [];
        }

        var playerIds = managedPlayers
            .Select(player => player.Id)
            .ToArray();
        var activeRegisteredPlayerIds = await dbContext.Registrations
            .AsNoTracking()
            .Where(registration => registration.OpportunityId == opportunityId)
            .Where(registration => playerIds.Contains(registration.PlayerId))
            .Select(registration => new
            {
                registration.PlayerId,
                registration.Status
            })
            .ToArrayAsync(cancellationToken);
        var activeRegisteredPlayerIdSet = activeRegisteredPlayerIds
            .Where(registration => ClassifyRegistrationStatus(registration.Status) != RegistrationStatusCategory.Declined)
            .Select(registration => registration.PlayerId)
            .Distinct()
            .ToHashSet();

        return managedPlayers
            .Select(player => new OpportunityRegistrationPlayerOptionPageItem(
                player.Id,
                player.DisplayName,
                activeRegisteredPlayerIdSet.Contains(player.Id),
                player.ProfileName,
                player.ProfileBirthDate,
                player.SchoolName,
                player.ContactPhone,
                player.ContactEmail))
            .ToArray();
    }

    private static void ApplySelectedPlayerRegistrationDefaults(
        TeamOpportunityRegistrationInputPageModel registrationForm,
        OpportunityRegistrationPlayerOptionPageItem? selectedPlayer)
    {
        if (selectedPlayer is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(registrationForm.PlayerName))
        {
            registrationForm.PlayerName = selectedPlayer.ProfileName;
        }

        if (registrationForm.PlayerBirthDate is null && selectedPlayer.ProfileBirthDate.HasValue)
        {
            registrationForm.PlayerBirthDate = selectedPlayer.ProfileBirthDate.Value.Date;
        }

        if (string.IsNullOrWhiteSpace(registrationForm.PlayerSchool))
        {
            registrationForm.PlayerSchool = selectedPlayer.ProfileSchool;
        }

        if (string.IsNullOrWhiteSpace(registrationForm.PlayerPhone))
        {
            registrationForm.PlayerPhone = selectedPlayer.ProfilePhone;
        }

        if (string.IsNullOrWhiteSpace(registrationForm.PlayerEmail))
        {
            registrationForm.PlayerEmail = selectedPlayer.ProfileEmail;
        }
    }

    private void ValidateOpportunityRegistrationForm(
        TeamOpportunityRegistrationInputPageModel registrationForm,
        IReadOnlyCollection<string> requiredRegistrationFieldCodes)
    {
        static string FieldKey(string fieldName) => BuildRegistrationFormModelStateKey(fieldName);

        bool RequiresField(string code)
        {
            return requiredRegistrationFieldCodes.Contains(code, StringComparer.OrdinalIgnoreCase);
        }

        if (RequiresField(TryOutSpotOpportunityRegistrationFields.PlayerName)
            && string.IsNullOrWhiteSpace(registrationForm.PlayerName))
        {
            ModelState.AddModelError(FieldKey(nameof(registrationForm.PlayerName)), "Player name is required.");
        }

        if (RequiresField(TryOutSpotOpportunityRegistrationFields.PlayerBirthDate)
            && registrationForm.PlayerBirthDate is null)
        {
            ModelState.AddModelError(FieldKey(nameof(registrationForm.PlayerBirthDate)), "Player birthdate is required.");
        }

        if (RequiresField(TryOutSpotOpportunityRegistrationFields.PlayerSchool)
            && string.IsNullOrWhiteSpace(registrationForm.PlayerSchool))
        {
            ModelState.AddModelError(FieldKey(nameof(registrationForm.PlayerSchool)), "Player school is required.");
        }

        if (RequiresField(TryOutSpotOpportunityRegistrationFields.PlayerPhone)
            && string.IsNullOrWhiteSpace(registrationForm.PlayerPhone))
        {
            ModelState.AddModelError(FieldKey(nameof(registrationForm.PlayerPhone)), "Player cell phone is required.");
        }

        if (RequiresField(TryOutSpotOpportunityRegistrationFields.PlayerEmail)
            && string.IsNullOrWhiteSpace(registrationForm.PlayerEmail))
        {
            ModelState.AddModelError(FieldKey(nameof(registrationForm.PlayerEmail)), "Player email address is required.");
        }

        if (RequiresField(TryOutSpotOpportunityRegistrationFields.GuardianName)
            && string.IsNullOrWhiteSpace(registrationForm.GuardianName))
        {
            ModelState.AddModelError(FieldKey(nameof(registrationForm.GuardianName)), "Guardian name is required.");
        }

        if (RequiresField(TryOutSpotOpportunityRegistrationFields.GuardianEmail)
            && string.IsNullOrWhiteSpace(registrationForm.GuardianEmail))
        {
            ModelState.AddModelError(FieldKey(nameof(registrationForm.GuardianEmail)), "Guardian email is required.");
        }

        if (RequiresField(TryOutSpotOpportunityRegistrationFields.GuardianPhone)
            && string.IsNullOrWhiteSpace(registrationForm.GuardianPhone))
        {
            ModelState.AddModelError(FieldKey(nameof(registrationForm.GuardianPhone)), "Guardian phone is required.");
        }

        if (RequiresField(TryOutSpotOpportunityRegistrationFields.EmergencyContactName)
            && string.IsNullOrWhiteSpace(registrationForm.EmergencyContactName))
        {
            ModelState.AddModelError(FieldKey(nameof(registrationForm.EmergencyContactName)), "Emergency contact name is required.");
        }

        if (RequiresField(TryOutSpotOpportunityRegistrationFields.EmergencyContactPhone)
            && string.IsNullOrWhiteSpace(registrationForm.EmergencyContactPhone))
        {
            ModelState.AddModelError(FieldKey(nameof(registrationForm.EmergencyContactPhone)), "Emergency contact phone is required.");
        }

        if (RequiresField(TryOutSpotOpportunityRegistrationFields.MedicalInfo)
            && string.IsNullOrWhiteSpace(registrationForm.MedicalInfo))
        {
            ModelState.AddModelError(FieldKey(nameof(registrationForm.MedicalInfo)), "Medical notes are required.");
        }

        if (RequiresField(TryOutSpotOpportunityRegistrationFields.WaiverSignature)
            && !registrationForm.WaiverAcknowledged)
        {
            ModelState.AddModelError(FieldKey(nameof(registrationForm.WaiverAcknowledged)), "Waiver acknowledgment is required.");
        }

        if (RequiresField(TryOutSpotOpportunityRegistrationFields.WaiverSignature)
            && registrationForm.WaiverAcknowledged
            && string.IsNullOrWhiteSpace(registrationForm.WaiverSignerName))
        {
            ModelState.AddModelError(FieldKey(nameof(registrationForm.WaiverSignerName)), "Waiver signer name is required.");
        }

        if (RequiresField(TryOutSpotOpportunityRegistrationFields.AdditionalNotes)
            && string.IsNullOrWhiteSpace(registrationForm.AdditionalNotes))
        {
            ModelState.AddModelError(FieldKey(nameof(registrationForm.AdditionalNotes)), "Additional notes are required.");
        }
    }

    private static string? BuildOpportunityRegistrationDataJson(
        IReadOnlyCollection<string> requiredRegistrationFieldCodes,
        string? playerName,
        DateTime? playerBirthDate,
        string? playerSchool,
        string? playerPhone,
        string? playerEmail,
        string? guardianName,
        string? guardianEmail,
        string? guardianPhone,
        string? additionalNotes,
        DateTime submittedAtUtc)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["submittedAtUtc"] = submittedAtUtc,
            ["requiredFieldCodes"] = requiredRegistrationFieldCodes
        };

        if (!string.IsNullOrWhiteSpace(playerName))
        {
            payload["playerName"] = playerName;
        }

        if (playerBirthDate.HasValue)
        {
            payload["playerBirthDate"] = playerBirthDate.Value.Date.ToString("yyyy-MM-dd");
        }

        if (!string.IsNullOrWhiteSpace(playerSchool))
        {
            payload["playerSchool"] = playerSchool;
        }

        if (!string.IsNullOrWhiteSpace(playerPhone))
        {
            payload["playerPhone"] = playerPhone;
        }

        if (!string.IsNullOrWhiteSpace(playerEmail))
        {
            payload["playerEmail"] = playerEmail;
        }

        if (!string.IsNullOrWhiteSpace(guardianName))
        {
            payload["guardianName"] = guardianName;
        }

        if (!string.IsNullOrWhiteSpace(guardianEmail))
        {
            payload["guardianEmail"] = guardianEmail;
        }

        if (!string.IsNullOrWhiteSpace(guardianPhone))
        {
            payload["guardianPhone"] = guardianPhone;
        }

        if (!string.IsNullOrWhiteSpace(additionalNotes))
        {
            payload["additionalNotes"] = additionalNotes;
        }

        return payload.Count == 0
            ? null
            : JsonSerializer.Serialize(payload);
    }

    private static string BuildRegistrationFormModelStateKey(string fieldName)
    {
        return $"{nameof(TeamOpportunityDetailPageModel.RegistrationForm)}.{fieldName}";
    }

    [AllowAnonymous]
    [HttpGet("/media/team-logos/{teamId:guid}")]
    public async Task<IActionResult> TeamLogo(
        Guid teamId,
        CancellationToken cancellationToken = default)
    {
        var team = await dbContext.Teams
            .AsNoTracking()
            .Where(currentTeam => currentTeam.Id == teamId)
            .Where(currentTeam => currentTeam.IsActive)
            .Where(currentTeam => currentTeam.IsSearchable)
            .SingleOrDefaultAsync(cancellationToken);
        if (team is null)
        {
            return NotFound();
        }

        var objectKey = TryGetStoredObjectKey(team.LogoImageUrl);
        if (objectKey is null)
        {
            return NotFound();
        }

        var payload = await imageStorageService.DownloadImageAsync(objectKey, cancellationToken);
        return payload is null
            ? NotFound()
            : File(payload.Content, payload.ContentType);
    }

    [AllowAnonymous]
    [HttpGet("/media/player-profiles/{playerId:guid}")]
    public async Task<IActionResult> PlayerProfileImage(
        Guid playerId,
        CancellationToken cancellationToken = default)
    {
        var player = await dbContext.Players
            .AsNoTracking()
            .Where(currentPlayer => currentPlayer.Id == playerId)
            .Where(currentPlayer => currentPlayer.IsActive)
            .SingleOrDefaultAsync(cancellationToken);
        if (player is null)
        {
            return NotFound();
        }

        var objectKey = TryGetStoredObjectKey(player.ProfileImageUrl);
        if (objectKey is null)
        {
            return NotFound();
        }

        var payload = await imageStorageService.DownloadImageAsync(objectKey, cancellationToken);
        return payload is null
            ? NotFound()
            : File(payload.Content, payload.ContentType);
    }

    [AllowAnonymous]
    [HttpGet("/listing-documents/{listingType}/{listingId:guid}")]
    public async Task<IActionResult> ListingDocument(
        string listingType,
        Guid listingId,
        CancellationToken cancellationToken = default)
    {
        var normalizedType = NormalizeListingType(listingType);
        if (normalizedType is null)
        {
            return NotFound();
        }

        var canAccessDocument = await CanAccessListingDocumentAsync(
            normalizedType,
            listingId,
            cancellationToken);
        if (!canAccessDocument)
        {
            return NotFound();
        }

        var documentReference = await GetPdfReferenceAsync(normalizedType, listingId, cancellationToken);
        if (documentReference is null || string.IsNullOrWhiteSpace(documentReference.Value.ObjectKey))
        {
            return NotFound();
        }

        var payload = await pdfStorageService.DownloadFileAsync(documentReference.Value.ObjectKey, cancellationToken);
        if (payload is null || payload.Content.Length == 0)
        {
            return NotFound();
        }

        var contentType = ResolveListingDocumentContentType(
            payload.ContentType,
            documentReference.Value.FileName,
            documentReference.Value.ObjectKey);

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(payload.Content, contentType, enableRangeProcessing: false);
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

        var roles = await GetCanonicalPublicRolesAsync(user);
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

        if (!TryOutSpotBillingCatalog.IsBillingIntervalSupported(selectedPlan.Code, billingInterval))
        {
            var message = TryOutSpotBillingCatalog.RequiresAnnualCommitment(selectedPlan.Code)
                ? "This plan requires annual billing."
                : "This plan does not offer the selected billing interval.";
            ModelState.AddModelError(nameof(model.BillingInterval), message);
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
            var activePaidMemberships = await GetActivePaidAccountMembershipsAsync(user.Id, cancellationToken);
            var cancelablePaidMemberships = activePaidMemberships
                .Where(subscription => !subscription.CancelAtPeriodEnd)
                .Where(subscription => !string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
                .ToArray();
            if (activePaidMemberships.Count > 0)
            {
                if (cancelablePaidMemberships.Length > 0 && !stripeBillingOptions.IsConfigured)
                {
                    ModelState.AddModelError(
                        nameof(model.PlanCode),
                        "Stripe billing is not configured, so the existing paid membership could not be scheduled for cancellation.");
                    return View(await BuildChoosePlanPageModelAsync(user, model, cancellationToken));
                }

                try
                {
                    var cancellationPeriodEnd = cancelablePaidMemberships.Length == 0
                        ? activePaidMemberships
                            .Where(subscription => subscription.CurrentPeriodEnd is not null)
                            .Select(subscription => subscription.CurrentPeriodEnd)
                            .OrderBy(date => date)
                            .FirstOrDefault()
                        : await SchedulePaidMembershipCancellationAtPeriodEndAsync(
                            user.Id,
                            cancelablePaidMemberships,
                            cancellationToken);

                    subscription.Status = "plan_selected";
                    subscription.CurrentPeriodStart = null;
                    subscription.CurrentPeriodEnd = null;
                    subscription.TrialEnd = null;
                    subscription.StripeCustomerId = null;
                    subscription.StripeSubscriptionId = null;
                    subscription.StripePriceId = null;
                    subscription.IsElite = false;
                    await dbContext.SaveChangesAsync(cancellationToken);

                    TempData["StatusMessage"] = cancelablePaidMemberships.Length == 0
                        ? cancellationPeriodEnd is null
                            ? "Your paid plan is already scheduled to end. Your account will move to free automatically when the current billing period closes."
                            : $"Your paid plan is already scheduled to end on {FormatDisplayDate(cancellationPeriodEnd.Value)}. Your account will move to free automatically."
                        : cancellationPeriodEnd is null
                            ? "Downgrade scheduled. Your paid plan will remain active until the current Stripe billing period ends, then move to free."
                            : $"Downgrade scheduled. Your paid plan remains active until {FormatDisplayDate(cancellationPeriodEnd.Value)}, then moves to free.";
                    return RedirectToAction(nameof(Settings));
                }
                catch (InvalidOperationException)
                {
                    ModelState.AddModelError(
                        nameof(model.PlanCode),
                        "We could not reach Stripe to schedule your cancellation. Try again or use the Stripe billing portal.");
                    return View(await BuildChoosePlanPageModelAsync(user, model, cancellationToken));
                }
            }

            subscription.Status = "active";
            subscription.CurrentPeriodStart = now;
            subscription.CurrentPeriodEnd = null;
            subscription.TrialEnd = null;
            subscription.StripeCustomerId = null;
            subscription.StripeSubscriptionId = null;
            subscription.StripePriceId = null;
            subscription.IsElite = false;
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
            BuildCheckoutIdempotencyKey(
                user.Id,
                selectedPlan.Code,
                billingInterval,
                TryOutSpotSubscriptionScopeTypes.Account,
                null),
            cancellationToken);

        subscription.Status = "checkout_started";
        subscription.StripeCustomerId = checkoutSession.StripeCustomerId;
        subscription.StripePriceId = stripePriceId;
        await dbContext.SaveChangesAsync(cancellationToken);

        return Redirect(checkoutSession.Url);
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpGet("settings")]
    public async Task<IActionResult> Settings(
        [FromQuery] string? billing,
        [FromQuery(Name = "session_id")] string? stripeCheckoutSessionId,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(Settings)) });
        }

        if (string.Equals(billing, "success", StringComparison.OrdinalIgnoreCase))
        {
            var sessionSuffix = string.IsNullOrWhiteSpace(stripeCheckoutSessionId)
                ? string.Empty
                : $" (session {TrimCheckoutSessionIdForDisplay(stripeCheckoutSessionId)})";
            TempData["StatusMessage"] = $"Checkout completed successfully{sessionSuffix}.";
        }
        else if (string.Equals(billing, "cancelled", StringComparison.OrdinalIgnoreCase))
        {
            TempData["StatusMessage"] = "Checkout was cancelled. Your membership has not changed.";
        }

        return View(await BuildAccountSettingsPageModelAsync(user, cancellationToken));
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpPost("settings/open-billing-portal")]
    public async Task<IActionResult> OpenBillingPortal(CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(Settings)) });
        }

        if (!stripeBillingOptions.IsConfigured)
        {
            TempData["StatusMessage"] = "Stripe billing is not configured yet.";
            return RedirectToAction(nameof(Settings));
        }

        var stripeCustomerId = await ResolveExistingStripeCustomerIdAsync(user.Id, cancellationToken);
        if (string.IsNullOrWhiteSpace(stripeCustomerId))
        {
            TempData["StatusMessage"] = "No paid subscription billing profile is linked yet. Choose a paid plan first.";
            return RedirectToAction(nameof(ChoosePlan));
        }

        try
        {
            var portalSession = await stripeBillingService.CreatePortalSessionAsync(
                stripeCustomerId,
                BuildCustomerPortalIdempotencyKey(user.Id),
                cancellationToken);
            return Redirect(portalSession.Url);
        }
        catch (InvalidOperationException)
        {
            TempData["StatusMessage"] = "Stripe billing portal is not available right now. Please try again.";
            return RedirectToAction(nameof(Settings));
        }
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpPost("settings/cancel-membership")]
    public async Task<IActionResult> CancelMembership(CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(Settings)) });
        }

        var activePaidMemberships = await GetActivePaidAccountMembershipsAsync(user.Id, cancellationToken);
        if (activePaidMemberships.Count == 0)
        {
            TempData["StatusMessage"] = "No active paid membership is available to cancel.";
            return RedirectToAction(nameof(Settings));
        }

        var cancelablePaidMemberships = activePaidMemberships
            .Where(subscription => !subscription.CancelAtPeriodEnd)
            .Where(subscription => !string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
            .ToArray();
        if (cancelablePaidMemberships.Length == 0)
        {
            var alreadyScheduledAt = activePaidMemberships
                .Where(subscription => subscription.CurrentPeriodEnd is not null)
                .Select(subscription => subscription.CurrentPeriodEnd)
                .OrderBy(date => date)
                .FirstOrDefault();
            TempData["StatusMessage"] = alreadyScheduledAt is null
                ? "Your paid cancellation is already scheduled."
                : $"Your paid cancellation is already scheduled for {FormatDisplayDate(alreadyScheduledAt.Value)}.";
            return RedirectToAction(nameof(Settings));
        }

        if (!stripeBillingOptions.IsConfigured)
        {
            TempData["StatusMessage"] = "Stripe billing is not configured right now. Use the Stripe billing portal or contact support.";
            return RedirectToAction(nameof(Settings));
        }

        try
        {
            var cancellationPeriodEnd = await SchedulePaidMembershipCancellationAtPeriodEndAsync(
                user.Id,
                cancelablePaidMemberships,
                cancellationToken);

            TempData["StatusMessage"] = cancellationPeriodEnd is null
                ? "Cancellation scheduled. Paid access remains active until the end of your current billing period."
                : $"Cancellation scheduled. Paid access remains active until {FormatDisplayDate(cancellationPeriodEnd.Value)}.";
        }
        catch (InvalidOperationException)
        {
            TempData["StatusMessage"] = "Stripe billing was unavailable, so cancellation could not be scheduled. Please try again.";
        }

        return RedirectToAction(nameof(Settings));
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
            return RedirectToAction(nameof(Login), new { returnUrl = ResolveLocalReturnUrl(model.ReturnUrl) ?? Url.Action(nameof(Settings)) });
        }

        var phoneNumber = NormalizeOptional(model.PhoneNumber) ?? user.PhoneNumber;
        if (string.IsNullOrWhiteSpace(phoneNumber))
        {
            TempData["StatusMessage"] = "Add a phone number before sending a verification code.";
            return RedirectToLocalOrSettings(model.ReturnUrl);
        }

        if (!user.SmsConsentAccepted)
        {
            TempData["StatusMessage"] = "SMS consent is required before sending phone verification messages.";
            return RedirectToLocalOrSettings(model.ReturnUrl);
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
        return RedirectToLocalOrSettings(model.ReturnUrl);
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpPost("settings/verify-phone")]
    public async Task<IActionResult> VerifyPhone(
        [Bind(Prefix = "Phone")] PhoneSettingsPageModel model)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = ResolveLocalReturnUrl(model.ReturnUrl) ?? Url.Action(nameof(Settings)) });
        }

        var phoneNumber = NormalizeOptional(model.PhoneNumber) ?? user.PhoneNumber;
        if (string.IsNullOrWhiteSpace(phoneNumber) || string.IsNullOrWhiteSpace(model.VerificationCode))
        {
            TempData["StatusMessage"] = "Enter the phone number and verification code.";
            return RedirectToLocalOrSettings(model.ReturnUrl);
        }

        var result = await userManager.ChangePhoneNumberAsync(user, phoneNumber, model.VerificationCode.Trim());
        if (!result.Succeeded)
        {
            TempData["StatusMessage"] = string.Join(" ", result.Errors.Select(error => error.Description));
            return RedirectToLocalOrSettings(model.ReturnUrl);
        }

        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);
        await SignInWebUserAsync(user, isPersistent: true);

        TempData["StatusMessage"] = "Phone number verified.";
        return RedirectToLocalOrSettings(model.ReturnUrl);
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
        [FromForm] List<string>? accountTypes,
        [FromForm] string? playerParentRole,
        [FromForm] string? teamRole,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(Settings)) });
        }

        var normalizationMessage = await NormalizeCurrentUserPublicRoleBundlesAsync(user, cancellationToken);
        var requestedAccountTypes = BuildRequestedAccountTypesFromBundles(accountTypes, playerParentRole, teamRole);
        var validatedAccountTypes = GetValidatedPublicAccountTypes(requestedAccountTypes, nameof(accountTypes));
        var result = await accountTypeChangeWorkflowService.QueueOrApplyAsync(user, validatedAccountTypes, cancellationToken);
        if (result.AppliedImmediately)
        {
            await SignInWebUserAsync(user, isPersistent: true);
        }
        TempData["StatusMessage"] = string.IsNullOrWhiteSpace(normalizationMessage)
            ? result.Message
            : $"{normalizationMessage} {result.Message}";
        return RedirectToAction(nameof(Settings));
    }

    [Authorize(AuthenticationSchemes = TryOutSpotAuthenticationSchemes.WebCookie)]
    [HttpPost("settings/dashboard-activity")]
    public async Task<IActionResult> UpdateDashboardActivityPreferences(
        [FromForm] List<string>? activityTypes,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentWebUserAsync();
        if (user is null)
        {
            return RedirectToAction(nameof(Login), new { returnUrl = Url.Action(nameof(Settings)) });
        }

        await dashboardActivityService.UpdatePreferencesAsync(
            user.Id,
            activityTypes ?? [],
            cancellationToken);

        TempData["StatusMessage"] = "Dashboard activity preferences updated.";
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

    private async Task<string> BuildExistingAccountRegistrationMessageAsync(User user)
    {
        var passwordlessSocialAccount = await GetPasswordlessSocialAccountInfoAsync(user);
        if (passwordlessSocialAccount is not null)
        {
            var providers = FormatProviderList(passwordlessSocialAccount.ProviderNames);
            return $"An account already exists for this email and uses {providers} sign-in. " +
                $"Use Continue with {providers} below, or use Forgot password to add an email/password sign-in.";
        }

        return "An account with this email already exists. Sign in instead, or use Forgot password if you need to reset your password.";
    }

    private async Task<PasswordlessSocialAccountInfo?> GetPasswordlessSocialAccountInfoAsync(User user)
    {
        if (await userManager.HasPasswordAsync(user))
        {
            return null;
        }

        var providerNames = (await userManager.GetLoginsAsync(user))
            .Select(login => TryOutSpotSocialLoginProviders.Normalize(login.LoginProvider))
            .Where(provider => provider is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(GetProviderDisplayName)
            .OrderBy(providerName => providerName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return providerNames.Length == 0
            ? null
            : new PasswordlessSocialAccountInfo(providerNames);
    }

    private static string BuildPasswordlessSocialLoginMessage(IReadOnlyList<string> providerNames)
    {
        var providers = FormatProviderList(providerNames);
        return $"This account was created with {providers}. " +
            $"Use Continue with {providers} below, or use Forgot password to add an email/password sign-in.";
    }

    private static string FormatProviderList(IReadOnlyList<string> providerNames)
    {
        return providerNames.Count switch
        {
            0 => "social sign-in",
            1 => providerNames[0],
            2 => $"{providerNames[0]} or {providerNames[1]}",
            _ => $"{string.Join(", ", providerNames.Take(providerNames.Count - 1))}, or {providerNames[^1]}"
        };
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

    private static bool IsExternalProviderConfigured(
        string provider,
        ExternalProviderAvailability availability)
    {
        return provider switch
        {
            TryOutSpotSocialLoginProviders.Google => availability.GoogleIsConfigured,
            TryOutSpotSocialLoginProviders.Facebook => availability.FacebookIsConfigured,
            TryOutSpotSocialLoginProviders.Apple => availability.AppleIsConfigured,
            _ => false
        };
    }

    private string GetExternalLoginFallbackPath(string? source, string? returnUrl)
    {
        object? routeValues = string.IsNullOrWhiteSpace(returnUrl)
            ? null
            : new { returnUrl };
        return string.Equals(source, "register", StringComparison.OrdinalIgnoreCase)
            ? Url.Action(nameof(Register), routeValues) ?? "/account/register"
            : Url.Action(nameof(Login), routeValues) ?? "/account/login";
    }

    private string BuildAbsoluteUrl(string pathAndQuery)
    {
        var baseUri = new Uri($"{Request.Scheme}://{Request.Host}{Request.PathBase}/");
        return new Uri(baseUri, pathAndQuery.TrimStart('/')).ToString();
    }

    private static string? BuildAndroidChromeIntentUrl(string absoluteUrl)
    {
        return Uri.TryCreate(absoluteUrl, UriKind.Absolute, out var uri)
            ? $"intent://{uri.Authority}{uri.PathAndQuery}#Intent;scheme={uri.Scheme};package=com.android.chrome;end"
            : null;
    }

    private static ExternalLoginBrowserDetection? DetectBlockedExternalLoginBrowser(
        string provider,
        string? userAgent)
    {
        if (!ProviderRequiresSystemBrowser(provider)
            || string.IsNullOrWhiteSpace(userAgent)
            || !IsMobileUserAgent(userAgent))
        {
            return null;
        }

        foreach (var signature in InAppBrowserSignatures)
        {
            if (userAgent.Contains(signature.Token, StringComparison.OrdinalIgnoreCase))
            {
                return new ExternalLoginBrowserDetection(
                    signature.BrowserDisplayName,
                    IsAndroidUserAgent(userAgent));
            }
        }

        if (IsAndroidWebViewUserAgent(userAgent) || IsIosWebViewUserAgent(userAgent))
        {
            return new ExternalLoginBrowserDetection(
                "this app's built-in browser",
                IsAndroidUserAgent(userAgent));
        }

        return null;
    }

    private static bool ProviderRequiresSystemBrowser(string provider)
    {
        return provider is TryOutSpotSocialLoginProviders.Google
            or TryOutSpotSocialLoginProviders.Facebook;
    }

    private static bool IsMobileUserAgent(string userAgent)
    {
        return userAgent.Contains("Mobile", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("iPod", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAndroidUserAgent(string userAgent)
    {
        return userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAndroidWebViewUserAgent(string userAgent)
    {
        return userAgent.Contains("; wv", StringComparison.OrdinalIgnoreCase)
            || (userAgent.Contains("Version/4.0", StringComparison.OrdinalIgnoreCase)
                && userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase)
                && userAgent.Contains("Chrome/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsIosWebViewUserAgent(string userAgent)
    {
        return userAgent.Contains("AppleWebKit", StringComparison.OrdinalIgnoreCase)
            && userAgent.Contains("Mobile", StringComparison.OrdinalIgnoreCase)
            && !userAgent.Contains("Safari/", StringComparison.OrdinalIgnoreCase)
            && !userAgent.Contains("CriOS", StringComparison.OrdinalIgnoreCase)
            && !userAgent.Contains("FxiOS", StringComparison.OrdinalIgnoreCase)
            && !userAgent.Contains("EdgiOS", StringComparison.OrdinalIgnoreCase)
            && !userAgent.Contains("OPiOS", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ExternalProviderAvailability(
        bool GoogleIsConfigured,
        bool FacebookIsConfigured,
        bool AppleIsConfigured);

    private sealed record ExternalLoginBrowserDetection(
        string BrowserDisplayName,
        bool IsAndroid);

    private sealed record PasswordlessSocialAccountInfo(
        IReadOnlyList<string> ProviderNames);

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

    private async Task<SearchTeamItemsPageModel> BuildSearchTeamItemsPageModelAsync(
        User user,
        SearchTeamItemsPageModel model,
        CancellationToken cancellationToken)
    {
        var entitlements = await entitlementService.GetEntitlementsAsync(user.Id, cancellationToken);
        var roles = entitlements?.AccountTypes ?? await GetCanonicalPublicRolesAsync(user);
        var featureCodes = entitlements?.FeatureCodes ?? [];
        var hasPlayerParentAccess = roles.Any(TryOutSpotRoles.IsPlayerParentRole)
            || featureCodes.Contains(TryOutSpotFeatureCodes.BrowseOpportunities, StringComparer.Ordinal);
        var hasAdvancedOpportunitySearch = featureCodes.Contains(
            TryOutSpotFeatureCodes.AdvancedOpportunitySearch,
            StringComparer.Ordinal);
        var maxRadiusMiles = hasAdvancedOpportunitySearch
            ? ZipRadiusSearchService.MaxRadiusMiles
            : FreeSearchMaxRadiusMiles;

        NormalizeSearchTeamItemsModel(model);
        model.CanSearchTeamItems = hasPlayerParentAccess;
        model.HasAdvancedOpportunitySearch = hasAdvancedOpportunitySearch;
        model.CanUseOpportunityTypeFilters = hasAdvancedOpportunitySearch;
        model.CanUseCompetitionLevelFilter = hasAdvancedOpportunitySearch;
        model.CanUseExpandedRadius = hasAdvancedOpportunitySearch;
        model.MaxRadiusMiles = maxRadiusMiles;
        model.AvailableSports = await BuildSportSelectionItemsAsync(model.SportId, cancellationToken);

        var normalizedType = ResolveOpportunityTypeFilter(model, hasAdvancedOpportunitySearch);
        model.AvailableOpportunityTypes = BuildOpportunityTypeSearchOptions(model.Type, hasAdvancedOpportunitySearch);
        model.AvailableRadiusOptions = BuildRadiusSearchOptions(model.RadiusMiles, maxRadiusMiles);

        if (!hasPlayerParentAccess)
        {
            return model;
        }

        if (model.EventDateFrom.HasValue
            && model.EventDateTo.HasValue
            && model.EventDateFrom.Value > model.EventDateTo.Value)
        {
            ModelState.AddModelError(
                nameof(SearchTeamItemsPageModel.EventDateFrom),
                "Start date cannot be after end date.");
        }

        if (!hasAdvancedOpportunitySearch && !string.IsNullOrWhiteSpace(model.CompetitionLevel))
        {
            model.CompetitionLevelFilterIgnored = true;
            model.CompetitionLevel = null;
        }

        var zipRadius = await ResolveSearchZipRadiusAsync(
            model.OriginZipCode,
            model.RadiusMiles,
            maxRadiusMiles,
            nameof(SearchTeamItemsPageModel.OriginZipCode),
            constrained: () => model.RadiusWasConstrained = true,
            cancellationToken);
        model.SearchOriginZipCode = zipRadius?.OriginZipCode;
        model.SearchRadiusMiles = zipRadius?.RadiusMiles;
        if (zipRadius is not null)
        {
            model.OriginZipCode = zipRadius.OriginZipCode;
            model.RadiusMiles = zipRadius.RadiusMiles;
            model.AvailableRadiusOptions = BuildRadiusSearchOptions(model.RadiusMiles, maxRadiusMiles);
        }

        if (!ModelState.IsValid)
        {
            return model;
        }

        var normalizedSearch = NormalizeOptional(model.Q);
        var query = BuildTeamItemSearchQuery(
            normalizedSearch,
            model.SportId,
            normalizedType,
            model.AgeGroup,
            model.CompetitionLevel,
            model.EventDateFrom,
            model.EventDateTo,
            model.City,
            model.State,
            zipRadius?.ZipCodes,
            hasAdvancedOpportunitySearch);

        model.TotalCount = await query.CountAsync(cancellationToken);
        model.TotalPages = Math.Max(1, (int)Math.Ceiling(model.TotalCount / (double)model.PageSize));
        if (model.Page > model.TotalPages)
        {
            model.Page = model.TotalPages;
        }

        var rows = await BuildOrderedTeamItemSearchRows(
                query,
                normalizedSearch,
                zipRadius,
                user.Id,
                hasSearch: !string.IsNullOrWhiteSpace(normalizedSearch),
                hasRadius: zipRadius is not null)
            .Skip((model.Page - 1) * model.PageSize)
            .Take(model.PageSize)
            .ToArrayAsync(cancellationToken);
        model.Results = rows
            .Select(row => ToTeamItemSearchResult(row, zipRadius?.DistanceByZipCode))
            .ToArray();

        if (zipRadius is not null)
        {
            model.RadiusSuggestions = await BuildTeamItemRadiusSuggestionsAsync(
                user.Id,
                normalizedSearch,
                model,
                normalizedType,
                hasAdvancedOpportunitySearch,
                zipRadius,
                maxRadiusMiles,
                cancellationToken);
        }

        return model;
    }

    private async Task<SearchPlayersPageModel> BuildSearchPlayersPageModelAsync(
        User user,
        SearchPlayersPageModel model,
        CancellationToken cancellationToken)
    {
        var entitlements = await entitlementService.GetEntitlementsAsync(user.Id, cancellationToken);
        var roles = entitlements?.AccountTypes ?? await GetCanonicalPublicRolesAsync(user);
        var featureCodes = entitlements?.FeatureCodes ?? [];
        var hasTeamAccess = roles.Any(TryOutSpotRoles.IsTeamBundleRole)
            || featureCodes.Contains(TryOutSpotFeatureCodes.BasicPlayerSearch, StringComparer.Ordinal)
            || featureCodes.Contains(TryOutSpotFeatureCodes.AdvancedPlayerSearch, StringComparer.Ordinal);
        var canViewCoachOnlyPlayerProfiles = roles.Any(TryOutSpotRoles.IsTeamBundleRole);
        var hasAdvancedPlayerSearch = featureCodes.Contains(
            TryOutSpotFeatureCodes.AdvancedPlayerSearch,
            StringComparer.Ordinal);
        var maxRadiusMiles = hasAdvancedPlayerSearch
            ? ZipRadiusSearchService.MaxRadiusMiles
            : FreeSearchMaxRadiusMiles;

        NormalizeSearchPlayersModel(model);
        model.CanSearchPlayers = hasTeamAccess;
        model.HasAdvancedPlayerSearch = hasAdvancedPlayerSearch;
        model.CanUseSkillLevelFilter = hasAdvancedPlayerSearch;
        model.CanUseExpandedRadius = hasAdvancedPlayerSearch;
        model.MaxRadiusMiles = maxRadiusMiles;
        model.AvailableSports = await BuildSportSelectionItemsAsync(model.SportId, cancellationToken);

        var normalizedListingType = ResolvePlayerListingTypeFilter(model);
        model.AvailableListingTypes = BuildPlayerListingTypeSearchOptions(model.ListingType);
        model.AvailableRadiusOptions = BuildRadiusSearchOptions(model.RadiusMiles, maxRadiusMiles);

        if (!hasTeamAccess)
        {
            return model;
        }

        if (model.MinAge.HasValue && model.MaxAge.HasValue && model.MinAge.Value > model.MaxAge.Value)
        {
            ModelState.AddModelError(
                nameof(SearchPlayersPageModel.MinAge),
                "Minimum age cannot exceed maximum age.");
        }

        if (model.MinPrice.HasValue && model.MaxPrice.HasValue && model.MinPrice.Value > model.MaxPrice.Value)
        {
            ModelState.AddModelError(
                nameof(SearchPlayersPageModel.MinPrice),
                "Minimum price cannot exceed maximum price.");
        }

        if (!hasAdvancedPlayerSearch && !string.IsNullOrWhiteSpace(model.SkillLevel))
        {
            model.SkillLevelFilterIgnored = true;
            model.SkillLevel = null;
        }

        var zipRadius = await ResolveSearchZipRadiusAsync(
            model.OriginZipCode,
            model.RadiusMiles,
            maxRadiusMiles,
            nameof(SearchPlayersPageModel.OriginZipCode),
            constrained: () => model.RadiusWasConstrained = true,
            cancellationToken);
        model.SearchOriginZipCode = zipRadius?.OriginZipCode;
        model.SearchRadiusMiles = zipRadius?.RadiusMiles;
        if (zipRadius is not null)
        {
            model.OriginZipCode = zipRadius.OriginZipCode;
            model.RadiusMiles = zipRadius.RadiusMiles;
            model.AvailableRadiusOptions = BuildRadiusSearchOptions(model.RadiusMiles, maxRadiusMiles);
        }

        if (!ModelState.IsValid)
        {
            return model;
        }

        var normalizedSearch = NormalizeOptional(model.Q);
        var query = BuildPlayerSearchQuery(
            normalizedSearch,
            normalizedListingType,
            model.SportId,
            model.MinAge,
            model.MaxAge,
            model.SkillLevel,
            model.City,
            model.State,
            model.MinPrice,
            model.MaxPrice,
            zipRadius?.ZipCodes,
            canViewCoachOnlyPlayerProfiles);

        model.TotalCount = await query.CountAsync(cancellationToken);
        model.TotalPages = Math.Max(1, (int)Math.Ceiling(model.TotalCount / (double)model.PageSize));
        if (model.Page > model.TotalPages)
        {
            model.Page = model.TotalPages;
        }

        var rows = await BuildOrderedPlayerSearchRows(
                query,
                normalizedSearch,
                zipRadius,
                user.Id,
                hasSearch: !string.IsNullOrWhiteSpace(normalizedSearch),
                hasRadius: zipRadius is not null)
            .Skip((model.Page - 1) * model.PageSize)
            .Take(model.PageSize)
            .ToArrayAsync(cancellationToken);
        model.Results = rows
            .Select(row => ToPlayerSearchResult(row, zipRadius?.DistanceByZipCode))
            .ToArray();

        if (zipRadius is not null)
        {
            model.RadiusSuggestions = await BuildPlayerRadiusSuggestionsAsync(
                user.Id,
                normalizedSearch,
                model,
                normalizedListingType,
                canViewCoachOnlyPlayerProfiles,
                zipRadius,
                maxRadiusMiles,
                cancellationToken);
        }

        return model;
    }

    private static void NormalizeSearchTeamItemsModel(SearchTeamItemsPageModel model)
    {
        model.Q = NormalizeOptional(model.Q);
        model.Type = NormalizeOptional(model.Type) ?? AllSearchFilterValue;
        model.AgeGroup = NormalizeOptional(model.AgeGroup);
        model.CompetitionLevel = NormalizeOptional(model.CompetitionLevel);
        model.OriginZipCode = NormalizeOptional(model.OriginZipCode);
        model.City = NormalizeOptional(model.City);
        model.State = NormalizeState(model.State);
        model.Page = Math.Max(model.Page, 1);
        model.PageSize = NormalizeSearchPageSize(model.PageSize);
    }

    private static void NormalizeSearchPlayersModel(SearchPlayersPageModel model)
    {
        model.Q = NormalizeOptional(model.Q);
        model.ListingType = NormalizeOptional(model.ListingType) ?? AllSearchFilterValue;
        model.SkillLevel = NormalizeOptional(model.SkillLevel);
        model.OriginZipCode = NormalizeOptional(model.OriginZipCode);
        model.City = NormalizeOptional(model.City);
        model.State = NormalizeState(model.State);
        model.Page = Math.Max(model.Page, 1);
        model.PageSize = NormalizeSearchPageSize(model.PageSize);
    }

    private string? ResolveOpportunityTypeFilter(
        SearchTeamItemsPageModel model,
        bool hasAdvancedOpportunitySearch)
    {
        var normalizedType = NormalizeSearchOptionCode(model.Type);
        if (!hasAdvancedOpportunitySearch)
        {
            if (!string.Equals(normalizedType, "tryout", StringComparison.Ordinal))
            {
                model.TypeFilterConstrained = true;
            }

            model.Type = "tryout";
            return "tryout";
        }

        if (string.IsNullOrWhiteSpace(normalizedType)
            || string.Equals(normalizedType, AllSearchFilterValue, StringComparison.Ordinal))
        {
            model.Type = AllSearchFilterValue;
            return null;
        }

        if (!TeamOpportunityTypeOptions.Contains(normalizedType, StringComparer.Ordinal))
        {
            ModelState.AddModelError(
                nameof(SearchTeamItemsPageModel.Type),
                "Choose one of the supported opportunity types.");
            model.Type = AllSearchFilterValue;
            return null;
        }

        model.Type = normalizedType;
        return normalizedType;
    }

    private string? ResolvePlayerListingTypeFilter(SearchPlayersPageModel model)
    {
        var normalizedListingType = NormalizeSearchOptionCode(model.ListingType);
        if (string.IsNullOrWhiteSpace(normalizedListingType)
            || string.Equals(normalizedListingType, AllSearchFilterValue, StringComparison.Ordinal))
        {
            model.ListingType = AllSearchFilterValue;
            return null;
        }

        var supportedListingType = NormalizePlayerListingType(
            normalizedListingType,
            nameof(SearchPlayersPageModel.ListingType));
        if (supportedListingType is null)
        {
            model.ListingType = AllSearchFilterValue;
            return null;
        }

        model.ListingType = supportedListingType;
        return supportedListingType;
    }

    private async Task<ZipRadiusSearchResult?> ResolveSearchZipRadiusAsync(
        string? originZipCode,
        int? radiusMiles,
        int maxRadiusMiles,
        string originZipCodeModelStateKey,
        Action constrained,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(originZipCode))
        {
            return null;
        }

        var normalizedOriginZipCode = zipRadiusSearchService.NormalizeZipCode(originZipCode);
        if (normalizedOriginZipCode is null)
        {
            ModelState.AddModelError(originZipCodeModelStateKey, "Enter a valid 5-digit ZIP code.");
            return null;
        }

        var normalizedRadiusMiles = zipRadiusSearchService.ClampRadiusMiles(radiusMiles);
        if (normalizedRadiusMiles > maxRadiusMiles)
        {
            normalizedRadiusMiles = maxRadiusMiles;
            constrained();
        }

        var zipRadiusResult = await zipRadiusSearchService.ResolveZipCodesWithinRadiusAsync(
            normalizedOriginZipCode,
            normalizedRadiusMiles,
            cancellationToken);
        if (zipRadiusResult is null)
        {
            ModelState.AddModelError(
                originZipCodeModelStateKey,
                "That ZIP code is not in the geographic catalog yet.");
        }

        return zipRadiusResult;
    }

    private IQueryable<Opportunity> BuildTeamItemSearchQuery(
        string? normalizedSearch,
        Guid? sportId,
        string? normalizedType,
        string? ageGroup,
        string? competitionLevel,
        DateTime? eventDateFrom,
        DateTime? eventDateTo,
        string? city,
        string? state,
        IReadOnlyCollection<string>? zipCodes,
        bool hasAdvancedOpportunitySearch)
    {
        var now = DateTime.UtcNow;
        var query = dbContext.Opportunities
            .AsNoTracking()
            .Where(opportunity => opportunity.IsActive)
            .Where(opportunity => opportunity.IsPublished)
            .Where(opportunity =>
                (opportunity.ListingEndDate ?? opportunity.ExpiresAt) == null
                || (opportunity.ListingEndDate ?? opportunity.ExpiresAt) > now)
            .Where(opportunity => opportunity.Team.IsActive)
            .Where(opportunity => opportunity.Team.IsSearchable);

        if (!hasAdvancedOpportunitySearch)
        {
            query = query.Where(opportunity => opportunity.ListingStartDate == null || opportunity.ListingStartDate <= now);
        }

        if (sportId.HasValue)
        {
            query = query.Where(opportunity => opportunity.SportId == sportId.Value);
        }

        if (!string.IsNullOrWhiteSpace(normalizedType))
        {
            query = query.Where(opportunity => opportunity.Type.ToLower() == normalizedType);
        }

        if (!string.IsNullOrWhiteSpace(ageGroup))
        {
            var ageGroupSearch = ageGroup.ToLowerInvariant();
            query = query.Where(opportunity =>
                opportunity.AgeGroup != null
                && opportunity.AgeGroup.ToLower().Contains(ageGroupSearch));
        }

        if (hasAdvancedOpportunitySearch && !string.IsNullOrWhiteSpace(competitionLevel))
        {
            var levelSearch = competitionLevel.ToLowerInvariant();
            query = query.Where(opportunity =>
                opportunity.CompetitionLevel != null
                && opportunity.CompetitionLevel.ToLower().Contains(levelSearch));
        }

        var normalizedEventDateFrom = NormalizeUtc(eventDateFrom);
        if (normalizedEventDateFrom.HasValue)
        {
            query = query.Where(opportunity =>
                opportunity.EventDate != null
                && opportunity.EventDate >= normalizedEventDateFrom.Value);
        }

        var normalizedEventDateTo = NormalizeUtc(eventDateTo);
        if (normalizedEventDateTo.HasValue)
        {
            query = query.Where(opportunity =>
                opportunity.EventDate != null
                && opportunity.EventDate <= normalizedEventDateTo.Value);
        }

        if (!string.IsNullOrWhiteSpace(city))
        {
            var citySearch = city.ToLowerInvariant();
            query = query.Where(opportunity =>
                (opportunity.City != null && opportunity.City.ToLower().Contains(citySearch))
                || (opportunity.City == null && opportunity.Team.City != null && opportunity.Team.City.ToLower().Contains(citySearch)));
        }

        if (!string.IsNullOrWhiteSpace(state))
        {
            query = query.Where(opportunity =>
                opportunity.State == state
                || (opportunity.State == null && opportunity.Team.State == state));
        }

        if (zipCodes is { Count: > 0 })
        {
            query = query.Where(opportunity =>
                (opportunity.ZipCode != null && zipCodes.Contains(opportunity.ZipCode))
                || (opportunity.ZipCode == null
                    && opportunity.Team.ZipCode != null
                    && zipCodes.Contains(opportunity.Team.ZipCode)));
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var search = normalizedSearch.ToLowerInvariant();
            query = query.Where(opportunity =>
                opportunity.Title.ToLower().Contains(search)
                || (opportunity.Description != null && opportunity.Description.ToLower().Contains(search))
                || (opportunity.City != null && opportunity.City.ToLower().Contains(search))
                || (opportunity.State != null && opportunity.State.ToLower().Contains(search))
                || (opportunity.ZipCode != null && opportunity.ZipCode.Contains(search))
                || opportunity.Team.Name.ToLower().Contains(search)
                || opportunity.Sport.Name.ToLower().Contains(search)
                || (opportunity.Team.Organization != null && opportunity.Team.Organization.Name.ToLower().Contains(search)));
        }

        return query;
    }

    private IQueryable<PlayerListing> BuildPlayerSearchQuery(
        string? normalizedSearch,
        string? normalizedListingType,
        Guid? sportId,
        int? minAge,
        int? maxAge,
        string? skillLevel,
        string? city,
        string? state,
        decimal? minPrice,
        decimal? maxPrice,
        IReadOnlyCollection<string>? zipCodes,
        bool canViewCoachOnlyPlayerProfiles)
    {
        var now = DateTime.UtcNow;
        var publicVisibility = PlayerContactVisibilityOptions[0];
        var coachOnlyVisibility = PlayerContactVisibilityOptions[1];
        var query = dbContext.PlayerListings
            .AsNoTracking()
            .Where(listing => listing.IsActive)
            .Where(listing => listing.IsPublished)
            .Where(listing => listing.IsSearchable)
            .Where(listing => listing.ExpiresAt == null || listing.ExpiresAt > now)
            .Where(listing =>
                listing.Player != null
                && listing.Player.IsActive
                    && listing.Player.IsSearchable
                    && (listing.Player.ContactVisibility == publicVisibility
                        || (canViewCoachOnlyPlayerProfiles
                            && listing.Player.ContactVisibility == coachOnlyVisibility)));

        if (!string.IsNullOrWhiteSpace(normalizedListingType))
        {
            query = query.Where(listing => listing.ListingType == normalizedListingType);
        }

        if (sportId.HasValue)
        {
            query = query.Where(listing => listing.SportId == sportId.Value);
        }

        if (minAge.HasValue || maxAge.HasValue)
        {
            var today = DateTime.UtcNow.Date;
            if (minAge.HasValue)
            {
                var maxDobForMinAge = today.AddYears(-minAge.Value);
                query = query.Where(listing =>
                    listing.Player != null
                    && listing.Player.DateOfBirth <= maxDobForMinAge);
            }

            if (maxAge.HasValue)
            {
                var minDobForMaxAge = today.AddYears(-(maxAge.Value + 1)).AddDays(1);
                query = query.Where(listing =>
                    listing.Player != null
                    && listing.Player.DateOfBirth >= minDobForMaxAge);
            }
        }

        if (!string.IsNullOrWhiteSpace(skillLevel))
        {
            var skillLevelSearch = skillLevel.ToLowerInvariant();
            query = query.Where(listing =>
                listing.Player != null
                && listing.Player.PlayerSports.Any(playerSport =>
                    playerSport.IsActive
                    && playerSport.SkillLevel != null
                    && playerSport.SkillLevel.ToLower().Contains(skillLevelSearch)));
        }

        if (!string.IsNullOrWhiteSpace(city))
        {
            var citySearch = city.ToLowerInvariant();
            query = query.Where(listing => listing.City != null && listing.City.ToLower().Contains(citySearch));
        }

        if (!string.IsNullOrWhiteSpace(state))
        {
            query = query.Where(listing => listing.State == state);
        }

        if (minPrice.HasValue)
        {
            query = query.Where(listing => listing.AskingPrice == null || listing.AskingPrice >= minPrice.Value);
        }

        if (maxPrice.HasValue)
        {
            query = query.Where(listing => listing.AskingPrice == null || listing.AskingPrice <= maxPrice.Value);
        }

        if (zipCodes is { Count: > 0 })
        {
            query = query.Where(listing =>
                listing.ZipCode != null
                && zipCodes.Contains(listing.ZipCode));
        }

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var search = normalizedSearch.ToLowerInvariant();
            query = query.Where(listing =>
                listing.Title.ToLower().Contains(search)
                || (listing.Description != null && listing.Description.ToLower().Contains(search))
                || (listing.City != null && listing.City.ToLower().Contains(search))
                || (listing.State != null && listing.State.ToLower().Contains(search))
                || (listing.ZipCode != null && listing.ZipCode.Contains(search))
                || (listing.Player != null && listing.Player.FirstName.ToLower().Contains(search))
                || (listing.Player != null && listing.Player.LastName.ToLower().Contains(search))
                || (listing.Sport != null && listing.Sport.Name.ToLower().Contains(search)));
        }

        return query;
    }

    private IQueryable<TeamItemSearchProjection> BuildOrderedTeamItemSearchRows(
        IQueryable<Opportunity> query,
        string? normalizedSearch,
        ZipRadiusSearchResult? zipRadius,
        Guid viewerUserId,
        bool hasSearch,
        bool hasRadius)
    {
        var search = normalizedSearch?.ToLowerInvariant();
        var originLatitude = zipRadius?.OriginLatitude ?? 0d;
        var originLongitude = zipRadius?.OriginLongitude ?? 0d;
        var longitudeScale = hasRadius
            ? Math.Cos(originLatitude * Math.PI / 180d)
            : 1d;

        var rows = from opportunity in query
                   join zip in dbContext.ZipCodeGeographies.AsNoTracking()
                       on (opportunity.ZipCode ?? opportunity.Team.ZipCode) equals zip.ZipCode into zipJoin
                   from zip in zipJoin.DefaultIfEmpty()
                   select new
                   {
                       OpportunityId = opportunity.Id,
                       opportunity.TeamId,
                       opportunity.Title,
                       opportunity.Type,
                       TeamName = opportunity.Team.Name,
                       OrganizationName = opportunity.Team.Organization == null ? null : opportunity.Team.Organization.Name,
                       SportName = opportunity.Sport.Name,
                       opportunity.Description,
                       opportunity.CompetitionLevel,
                       opportunity.AgeGroup,
                       opportunity.RegistrationFee,
                       opportunity.RegistrationDeadline,
                       opportunity.EventDate,
                       opportunity.EventEndDate,
                       City = opportunity.City ?? opportunity.Team.City,
                       State = opportunity.State ?? opportunity.Team.State,
                       ZipCode = opportunity.ZipCode ?? opportunity.Team.ZipCode,
                       opportunity.PublishedAt,
                       DistanceSort = hasRadius && zip != null
                           ? (double?)((((double)zip.Latitude - originLatitude) * ((double)zip.Latitude - originLatitude))
                               + ((((double)zip.Longitude - originLongitude) * longitudeScale)
                                   * (((double)zip.Longitude - originLongitude) * longitudeScale)))
                           : null,
                       IsFavorited = dbContext.UserFavorites.Any(favorite =>
                           favorite.UserId == viewerUserId
                           && favorite.OpportunityId == opportunity.Id),
                       RelevanceScore = search == null
                           ? 0
                           : (opportunity.Title.ToLower().Contains(search) ? 10 : 0)
                               + (opportunity.Team.Name.ToLower().Contains(search) ? 7 : 0)
                               + (opportunity.Sport.Name.ToLower().Contains(search) ? 5 : 0)
                               + (opportunity.Description != null && opportunity.Description.ToLower().Contains(search) ? 4 : 0)
                               + (opportunity.City != null && opportunity.City.ToLower().Contains(search) ? 2 : 0)
                               + (opportunity.State != null && opportunity.State.ToLower().Contains(search) ? 1 : 0)
                               + (opportunity.ZipCode != null && opportunity.ZipCode.Contains(search) ? 1 : 0)
                   };

        var ordered = hasSearch
            ? rows.OrderByDescending(row => row.RelevanceScore)
            : hasRadius
                ? rows.OrderBy(row => row.DistanceSort ?? 999999d)
                : rows.OrderBy(row => row.EventDate ?? DateTime.MaxValue);

        if (hasSearch && hasRadius)
        {
            ordered = ordered.ThenBy(row => row.DistanceSort ?? 999999d);
        }

        return ordered
            .ThenBy(row => row.EventDate ?? DateTime.MaxValue)
            .ThenByDescending(row => row.PublishedAt)
            .Select(row => new TeamItemSearchProjection(
                row.OpportunityId,
                row.TeamId,
                row.Title,
                row.Type,
                row.TeamName,
                row.OrganizationName,
                row.SportName,
                row.Description,
                row.CompetitionLevel,
                row.AgeGroup,
                row.RegistrationFee,
                row.RegistrationDeadline,
                row.EventDate,
                row.EventEndDate,
                row.City,
                row.State,
                row.ZipCode,
                row.PublishedAt,
                row.DistanceSort,
                row.IsFavorited,
                row.RelevanceScore));
    }

    private IQueryable<PlayerSearchProjection> BuildOrderedPlayerSearchRows(
        IQueryable<PlayerListing> query,
        string? normalizedSearch,
        ZipRadiusSearchResult? zipRadius,
        Guid viewerUserId,
        bool hasSearch,
        bool hasRadius)
    {
        var search = normalizedSearch?.ToLowerInvariant();
        var originLatitude = zipRadius?.OriginLatitude ?? 0d;
        var originLongitude = zipRadius?.OriginLongitude ?? 0d;
        var longitudeScale = hasRadius
            ? Math.Cos(originLatitude * Math.PI / 180d)
            : 1d;
        var entitlingStatusActive = "active";
        var entitlingStatusTrialing = "trialing";
        var now = DateTime.UtcNow;

        var rows = from listing in query
                   join zip in dbContext.ZipCodeGeographies.AsNoTracking()
                       on listing.ZipCode equals zip.ZipCode into zipJoin
                   from zip in zipJoin.DefaultIfEmpty()
                   select new
                   {
                       ListingId = listing.Id,
                       listing.ListingType,
                       listing.Title,
                       listing.Description,
                       listing.PlayerId,
                       PlayerName = listing.Player == null ? null : (listing.Player.FirstName + " " + listing.Player.LastName).Trim(),
                       PlayerDateOfBirth = listing.Player == null ? null : (DateTime?)listing.Player.DateOfBirth,
                       SchoolName = listing.Player == null ? null : listing.Player.SchoolName,
                       CurrentTeamName = listing.Player == null ? null : listing.Player.CurrentTeamName,
                       GraduationYear = listing.Player == null ? null : listing.Player.GraduationYear,
                       Height = listing.Player == null ? null : listing.Player.Height,
                       Weight = listing.Player == null ? null : listing.Player.Weight,
                       ThrowsHand = listing.Player == null ? null : listing.Player.ThrowsHand,
                       BatsHand = listing.Player == null ? null : listing.Player.BatsHand,
                       SportName = listing.Sport == null ? null : listing.Sport.Name,
                       listing.AskingPrice,
                       listing.Currency,
                       listing.Condition,
                       listing.City,
                       listing.State,
                       listing.ZipCode,
                       listing.PublishedAt,
                       DistanceSort = hasRadius && zip != null
                           ? (double?)((((double)zip.Latitude - originLatitude) * ((double)zip.Latitude - originLatitude))
                               + ((((double)zip.Longitude - originLongitude) * longitudeScale)
                                   * (((double)zip.Longitude - originLongitude) * longitudeScale)))
                           : null,
                       IsPriorityListing = dbContext.Subscriptions.Any(subscription =>
                           subscription.UserId == listing.UserId
                           && (subscription.Status == entitlingStatusActive || subscription.Status == entitlingStatusTrialing)
                           && (subscription.PlanType == TryOutSpotPlanCodes.PremiumPlayer || subscription.IsElite))
                           || dbContext.ComplimentaryPlanGrants.Any(grant =>
                               grant.UserId == listing.UserId
                               && grant.PlanType == TryOutSpotPlanCodes.PremiumPlayer
                               && grant.RevokedAt == null
                               && grant.StartsAt <= now
                               && (grant.EndsAt == null || grant.EndsAt > now)),
                       IsFavorited = dbContext.UserFavorites.Any(favorite =>
                           favorite.UserId == viewerUserId
                           && favorite.PlayerListingId == listing.Id),
                       RelevanceScore = search == null
                           ? 0
                           : (listing.Title.ToLower().Contains(search) ? 10 : 0)
                               + (listing.Player != null && listing.Player.LastName.ToLower().Contains(search) ? 8 : 0)
                               + (listing.Player != null && listing.Player.FirstName.ToLower().Contains(search) ? 8 : 0)
                               + (listing.Sport != null && listing.Sport.Name.ToLower().Contains(search) ? 5 : 0)
                               + (listing.Description != null && listing.Description.ToLower().Contains(search) ? 4 : 0)
                               + (listing.City != null && listing.City.ToLower().Contains(search) ? 2 : 0)
                               + (listing.State != null && listing.State.ToLower().Contains(search) ? 1 : 0)
                               + (listing.ZipCode != null && listing.ZipCode.Contains(search) ? 1 : 0)
                   };

        var ordered = hasSearch
            ? rows.OrderByDescending(row => row.RelevanceScore)
            : rows.OrderByDescending(row => row.IsPriorityListing);

        if (hasSearch)
        {
            ordered = ordered.ThenByDescending(row => row.IsPriorityListing);
        }

        if (hasRadius)
        {
            ordered = ordered.ThenBy(row => row.DistanceSort ?? 999999d);
        }

        return ordered
            .ThenByDescending(row => row.PublishedAt)
            .Select(row => new PlayerSearchProjection(
                row.ListingId,
                row.ListingType,
                row.Title,
                row.Description,
                row.PlayerId,
                row.PlayerName,
                row.PlayerDateOfBirth,
                row.SchoolName,
                row.CurrentTeamName,
                row.GraduationYear,
                row.Height,
                row.Weight,
                row.ThrowsHand,
                row.BatsHand,
                row.SportName,
                row.AskingPrice,
                row.Currency,
                row.Condition,
                row.City,
                row.State,
                row.ZipCode,
                row.PublishedAt,
                row.DistanceSort,
                row.IsPriorityListing,
                row.IsFavorited,
                row.RelevanceScore));
    }

    private async Task<IReadOnlyCollection<TeamItemSearchSuggestionGroupPageItem>> BuildTeamItemRadiusSuggestionsAsync(
        Guid viewerUserId,
        string? normalizedSearch,
        SearchTeamItemsPageModel model,
        string? normalizedType,
        bool hasAdvancedOpportunitySearch,
        ZipRadiusSearchResult currentRadius,
        int maxRadiusMiles,
        CancellationToken cancellationToken)
    {
        var suggestions = new List<TeamItemSearchSuggestionGroupPageItem>();
        var alreadyCoveredZipCodes = currentRadius.ZipCodes.ToHashSet(StringComparer.Ordinal);
        foreach (var radiusMiles in GetSearchSuggestionRadii(currentRadius.RadiusMiles, maxRadiusMiles))
        {
            var expandedRadius = await zipRadiusSearchService.ResolveZipCodesWithinRadiusAsync(
                currentRadius.OriginZipCode,
                radiusMiles,
                cancellationToken);
            if (expandedRadius is null)
            {
                continue;
            }

            var additionalZipCodes = expandedRadius.ZipCodes
                .Where(zipCode => !alreadyCoveredZipCodes.Contains(zipCode))
                .ToArray();
            alreadyCoveredZipCodes.UnionWith(expandedRadius.ZipCodes);
            if (additionalZipCodes.Length == 0)
            {
                continue;
            }

            var query = BuildTeamItemSearchQuery(
                normalizedSearch,
                model.SportId,
                normalizedType,
                model.AgeGroup,
                model.CompetitionLevel,
                model.EventDateFrom,
                model.EventDateTo,
                model.City,
                model.State,
                additionalZipCodes,
                hasAdvancedOpportunitySearch);
            var rows = await BuildOrderedTeamItemSearchRows(
                    query,
                    normalizedSearch,
                    expandedRadius,
                    viewerUserId,
                    hasSearch: !string.IsNullOrWhiteSpace(normalizedSearch),
                    hasRadius: true)
                .Take(SearchSuggestionResultLimit)
                .ToArrayAsync(cancellationToken);
            if (rows.Length > 0)
            {
                suggestions.Add(new TeamItemSearchSuggestionGroupPageItem(
                    radiusMiles,
                    rows.Select(row => ToTeamItemSearchResult(row, expandedRadius.DistanceByZipCode)).ToArray()));
            }
        }

        return suggestions;
    }

    private async Task<IReadOnlyCollection<PlayerSearchSuggestionGroupPageItem>> BuildPlayerRadiusSuggestionsAsync(
        Guid viewerUserId,
        string? normalizedSearch,
        SearchPlayersPageModel model,
        string? normalizedListingType,
        bool canViewCoachOnlyPlayerProfiles,
        ZipRadiusSearchResult currentRadius,
        int maxRadiusMiles,
        CancellationToken cancellationToken)
    {
        var suggestions = new List<PlayerSearchSuggestionGroupPageItem>();
        var alreadyCoveredZipCodes = currentRadius.ZipCodes.ToHashSet(StringComparer.Ordinal);
        foreach (var radiusMiles in GetSearchSuggestionRadii(currentRadius.RadiusMiles, maxRadiusMiles))
        {
            var expandedRadius = await zipRadiusSearchService.ResolveZipCodesWithinRadiusAsync(
                currentRadius.OriginZipCode,
                radiusMiles,
                cancellationToken);
            if (expandedRadius is null)
            {
                continue;
            }

            var additionalZipCodes = expandedRadius.ZipCodes
                .Where(zipCode => !alreadyCoveredZipCodes.Contains(zipCode))
                .ToArray();
            alreadyCoveredZipCodes.UnionWith(expandedRadius.ZipCodes);
            if (additionalZipCodes.Length == 0)
            {
                continue;
            }

            var query = BuildPlayerSearchQuery(
                normalizedSearch,
                normalizedListingType,
                model.SportId,
                model.MinAge,
                model.MaxAge,
                model.SkillLevel,
                model.City,
                model.State,
                model.MinPrice,
                model.MaxPrice,
                additionalZipCodes,
                canViewCoachOnlyPlayerProfiles);
            var rows = await BuildOrderedPlayerSearchRows(
                    query,
                    normalizedSearch,
                    expandedRadius,
                    viewerUserId,
                    hasSearch: !string.IsNullOrWhiteSpace(normalizedSearch),
                    hasRadius: true)
                .Take(SearchSuggestionResultLimit)
                .ToArrayAsync(cancellationToken);
            if (rows.Length > 0)
            {
                suggestions.Add(new PlayerSearchSuggestionGroupPageItem(
                    radiusMiles,
                    rows.Select(row => ToPlayerSearchResult(row, expandedRadius.DistanceByZipCode)).ToArray()));
            }
        }

        return suggestions;
    }

    private async Task<IReadOnlyCollection<SportSelectionPageItem>> BuildSportSelectionItemsAsync(
        Guid? selectedSportId,
        CancellationToken cancellationToken)
    {
        return await dbContext.Sports
            .AsNoTracking()
            .Where(sport => sport.IsActive)
            .OrderBy(sport => sport.Name)
            .Select(sport => new SportSelectionPageItem(
                sport.Id,
                sport.Name,
                selectedSportId.HasValue && selectedSportId.Value == sport.Id))
            .ToArrayAsync(cancellationToken);
    }

    private static IReadOnlyCollection<SearchFilterOptionPageItem> BuildOpportunityTypeSearchOptions(
        string? selectedType,
        bool hasAdvancedOpportunitySearch)
    {
        var normalizedSelectedType = NormalizeSearchOptionCode(selectedType) ?? AllSearchFilterValue;
        var options = new List<SearchFilterOptionPageItem>
        {
            new(
                AllSearchFilterValue,
                "All types",
                string.Equals(normalizedSelectedType, AllSearchFilterValue, StringComparison.Ordinal),
                hasAdvancedOpportunitySearch,
                hasAdvancedOpportunitySearch ? null : "Premium Player unlocks all opportunity types.")
        };

        options.AddRange(TeamOpportunityTypeOptions.Select(type => new SearchFilterOptionPageItem(
            type,
            FormatSearchOptionLabel(type),
            string.Equals(normalizedSelectedType, type, StringComparison.Ordinal),
            hasAdvancedOpportunitySearch || string.Equals(type, "tryout", StringComparison.Ordinal),
            hasAdvancedOpportunitySearch || string.Equals(type, "tryout", StringComparison.Ordinal)
                ? null
                : "Premium Player unlocks this filter.")));

        return options;
    }

    private static IReadOnlyCollection<SearchFilterOptionPageItem> BuildPlayerListingTypeSearchOptions(
        string? selectedListingType)
    {
        var normalizedSelectedListingType = NormalizeSearchOptionCode(selectedListingType) ?? AllSearchFilterValue;
        var options = new List<SearchFilterOptionPageItem>
        {
            new(
                AllSearchFilterValue,
                "All listing types",
                string.Equals(normalizedSelectedListingType, AllSearchFilterValue, StringComparison.Ordinal),
                true)
        };

        options.AddRange(PlayerListingTypeOptions.Select(option => new SearchFilterOptionPageItem(
            option.Code,
            option.Label,
            string.Equals(normalizedSelectedListingType, option.Code, StringComparison.Ordinal),
            true)));

        return options;
    }

    private static IReadOnlyCollection<SearchRadiusOptionPageItem> BuildRadiusSearchOptions(
        int? selectedRadiusMiles,
        int maxRadiusMiles)
    {
        var selected = selectedRadiusMiles ?? ZipRadiusSearchService.DefaultRadiusMiles;
        return SearchRadiusOptions
            .Where(radius => radius <= maxRadiusMiles || radius == selected)
            .Select(radius => new SearchRadiusOptionPageItem(
                radius,
                $"{radius} miles",
                radius == selected,
                radius <= maxRadiusMiles))
            .ToArray();
    }

    private static IReadOnlyCollection<int> GetSearchSuggestionRadii(int currentRadiusMiles, int maxRadiusMiles)
    {
        return SearchSuggestionRadiusMiles
            .Where(radiusMiles => radiusMiles > currentRadiusMiles && radiusMiles <= maxRadiusMiles)
            .Take(3)
            .ToArray();
    }

    private static TeamItemSearchResultPageItem ToTeamItemSearchResult(
        TeamItemSearchProjection row,
        IReadOnlyDictionary<string, double>? distanceByZipCode)
    {
        return new TeamItemSearchResultPageItem(
            row.OpportunityId,
            row.TeamId,
            row.Title,
            row.Type,
            FormatSearchOptionLabel(row.Type),
            row.TeamName,
            row.OrganizationName,
            row.SportName,
            row.Description,
            row.CompetitionLevel,
            row.AgeGroup,
            row.RegistrationFee,
            row.RegistrationDeadline,
            row.EventDate,
            row.EventEndDate,
            row.City,
            row.State,
            row.ZipCode,
            ResolveDistanceMiles(row.ZipCode, distanceByZipCode),
            row.IsFavorited,
            row.RelevanceScore);
    }

    private static PlayerSearchResultPageItem ToPlayerSearchResult(
        PlayerSearchProjection row,
        IReadOnlyDictionary<string, double>? distanceByZipCode)
    {
        return new PlayerSearchResultPageItem(
            row.ListingId,
            row.ListingType,
            GetPlayerListingTypeLabel(row.ListingType),
            row.Title,
            row.Description,
            row.PlayerId,
            row.PlayerName,
            CalculateAge(row.PlayerDateOfBirth),
            row.PlayerDateOfBirth,
            row.SchoolName,
            row.CurrentTeamName,
            row.GraduationYear,
            row.Height,
            row.Weight,
            row.ThrowsHand,
            row.BatsHand,
            row.SportName,
            row.AskingPrice,
            row.Currency,
            row.Condition,
            row.City,
            row.State,
            row.ZipCode,
            ResolveDistanceMiles(row.ZipCode, distanceByZipCode),
            row.IsPriorityListing,
            row.IsFavorited,
            row.RelevanceScore);
    }

    private static int NormalizeSearchPageSize(int pageSize)
    {
        if (pageSize <= 0)
        {
            return DefaultSearchPageSize;
        }

        return Math.Clamp(pageSize, 1, MaxSearchPageSize);
    }

    private static int NormalizeDashboardActivityPage(int page)
    {
        return Math.Max(1, page);
    }

    private static int NormalizeDashboardActivityPageSize(int pageSize)
    {
        if (pageSize <= 0)
        {
            return DefaultDashboardRecentActivityPageSize;
        }

        return Math.Clamp(pageSize, 1, MaxDashboardRecentActivityPageSize);
    }

    private static int? CalculateAge(DateTime? dateOfBirth)
    {
        if (!dateOfBirth.HasValue)
        {
            return null;
        }

        var today = DateTime.UtcNow.Date;
        var birthDate = dateOfBirth.Value.Date;
        var age = today.Year - birthDate.Year;
        if (birthDate > today.AddYears(-age))
        {
            age--;
        }

        return Math.Max(age, 0);
    }

    private static double? ResolveDistanceMiles(
        string? zipCode,
        IReadOnlyDictionary<string, double>? distanceByZipCode)
    {
        if (distanceByZipCode is null || string.IsNullOrWhiteSpace(zipCode))
        {
            return null;
        }

        return distanceByZipCode.TryGetValue(zipCode, out var distanceMiles)
            ? Math.Round(distanceMiles, 1)
            : null;
    }

    private static string? NormalizeSearchOptionCode(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Replace("-", "_", StringComparison.Ordinal).Replace(" ", "_", StringComparison.Ordinal).ToLowerInvariant();
    }

    private static string FormatSearchOptionLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Item";
        }

        var normalized = value.Trim().Replace("_", " ", StringComparison.Ordinal).ToLowerInvariant();
        return normalized switch
        {
            "tryout" => "Tryout",
            "roster opening" => "Roster opening",
            "pickup player" => "Pickup player",
            "camp" => "Camp",
            "clinic" => "Clinic",
            "tournament" => "Tournament",
            "private workout" => "Private workout",
            _ => char.ToUpperInvariant(normalized[0]) + normalized[1..]
        };
    }

    private static bool TryNormalizeTeamOpportunityType(string? value, out string normalizedType)
    {
        var normalizedValue = NormalizeOptional(value)?
            .Replace("-", "_", StringComparison.Ordinal)
            .Replace(" ", "_", StringComparison.Ordinal)
            .ToLowerInvariant();

        normalizedType = TeamOpportunityTypeOptions.FirstOrDefault(option =>
            string.Equals(option, normalizedValue, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(normalizedType);
    }

    private async Task<OnboardingPageModel> BuildOnboardingPageModelAsync(
        User user,
        int activityPage,
        int activityPageSize,
        CancellationToken cancellationToken)
    {
        var roles = await GetCanonicalPublicRolesAsync(user);
        var entitlements = await entitlementService.GetEntitlementsAsync(user.Id, cancellationToken);
        var hasPlayerOrParentRole = HasAnyRole(roles, TryOutSpotRoles.Parent, TryOutSpotRoles.Player);
        var hasParentRole = HasAnyRole(roles, TryOutSpotRoles.Parent);
        var hasSelfPlayerRole = HasAnyRole(roles, TryOutSpotRoles.Player);
        var hasTeamOrOrganizationRole = roles.Any(TryOutSpotRoles.IsTeamBundleRole);
        var upcomingTryoutRegistrations = hasPlayerOrParentRole
            ? await GetOnboardingTryoutRegistrationsAsync(user.Id, cancellationToken)
            : [];
        var favoritePlayerListings = await GetDashboardPlayerListingFavoritesAsync(
            user.Id,
            DashboardFavoritePreviewLimit,
            cancellationToken);
        var favoriteOpportunities = await GetDashboardOpportunityFavoritesAsync(
            user.Id,
            DashboardFavoritePreviewLimit,
            cancellationToken);
        var favoriteCount = favoritePlayerListings.Count + favoriteOpportunities.Count;
        var recentActivity = await dashboardActivityService.GetRecentActivityAsync(
            user.Id,
            activityPage,
            activityPageSize,
            cancellationToken);

        var hasLinkedPlayers = hasPlayerOrParentRole
            && await dbContext.UserPlayerRelationships
                .AsNoTracking()
                .AnyAsync(
                    relationship => relationship.UserId == user.Id
                        && relationship.Player.IsActive,
                    cancellationToken);
        var hasPlayerListingManagementAccess = entitlements?.FeatureCodes.Contains(
                TryOutSpotFeatureCodes.CreatePlayerListings,
                StringComparer.Ordinal) == true;
        var hasManagedPlayerListings = false;
        if (hasPlayerOrParentRole && hasPlayerListingManagementAccess)
        {
            try
            {
                hasManagedPlayerListings = await dbContext.PlayerListings
                    .AsNoTracking()
                    .AnyAsync(listing => listing.UserId == user.Id && listing.IsActive, cancellationToken);
            }
            catch (PostgresException exception) when (IsUndefinedTableException(exception))
            {
                // Keeps onboarding alive while an environment catches up on migrations.
                hasPlayerListingManagementAccess = false;
                hasManagedPlayerListings = false;
            }
        }
        var hasTeamRole = hasTeamOrOrganizationRole
            && await dbContext.UserTeamRoles
                .AsNoTracking()
                .AnyAsync(teamRole => teamRole.UserId == user.Id, cancellationToken);
        var hasManagedTeamOpportunities = hasTeamRole
            && await dbContext.Opportunities
                .AsNoTracking()
                .AnyAsync(opportunity =>
                    opportunity.IsActive
                    && opportunity.Team.UserTeamRoles.Any(teamRole =>
                        teamRole.UserId == user.Id
                        && teamRole.IsActive),
                    cancellationToken);
        var subscriptions = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(currentSubscription => currentSubscription.UserId == user.Id)
            .ToArrayAsync(cancellationToken);
        var recommendedPlans = GetPlansForAccountTypes(roles);
        var hasCompletedPlanSelection = HasEntitlingRecommendedPlanSubscription(subscriptions, recommendedPlans);
        var hasTeamProfileManagementAccess = entitlements?.FeatureCodes.Contains(
                TryOutSpotFeatureCodes.PostLimitedOpportunities,
                StringComparer.Ordinal) == true
            || entitlements?.FeatureCodes.Contains(
                TryOutSpotFeatureCodes.UnlimitedOpportunityPostings,
                StringComparer.Ordinal) == true;
        var launchPromotion = await BuildLaunchPromotionPageItemAsync(
            user.Id,
            roles,
            user.EmailConfirmed,
            cancellationToken);

        var steps = new List<OnboardingStepPageItem>
        {
            new(
                "choose_account_types",
                "Choose account type",
                "Select how you plan to use TryOutSpot.",
                true,
                roles.Count > 0),
            new(
                "verify_email",
                "Verify email",
                "Email verification protects account recovery and sensitive account changes.",
                true,
                user.EmailConfirmed)
        };

        if (hasPlayerOrParentRole)
        {
            var playerProfileStepTitle = hasParentRole && !hasSelfPlayerRole
                ? "Add child/player profile"
                : "Complete player profile";
            var playerProfileStepDescription = hasParentRole && !hasSelfPlayerRole
                ? "Add the player's name, birth date, sports, and visibility. This is not your parent/guardian account profile."
                : "Add your player profile before registering for tryouts.";
            steps.Add(new OnboardingStepPageItem(
                "add_player_profile",
                playerProfileStepTitle,
                playerProfileStepDescription,
                false,
                hasLinkedPlayers));
            if (hasPlayerListingManagementAccess)
            {
                steps.Add(new OnboardingStepPageItem(
                    "manage_player_listings",
                    "Manage player listings",
                    "Create and manage pickup, team search, equipment, lesson, and training partner listings.",
                    false,
                    hasManagedPlayerListings));
            }
        }

        if (hasTeamOrOrganizationRole && hasTeamProfileManagementAccess)
        {
            steps.Add(new OnboardingStepPageItem(
                "add_team_or_organization",
                "Team / Organization Details",
                "Create or join a team or organization before posting opportunities.",
                false,
                hasTeamRole));
            steps.Add(new OnboardingStepPageItem(
                "manage_team_opportunities",
                "Team Listings",
                "Create, edit, publish, and deactivate team listings.",
                false,
                hasManagedTeamOpportunities));
        }

        if (hasPlayerOrParentRole || hasTeamOrOrganizationRole || favoriteCount > 0)
        {
            steps.Add(new OnboardingStepPageItem(
                "my_favorites",
                "My Favorites",
                "Review saved player listings and team opportunity listings.",
                false,
                favoriteCount > 0));
        }

        if (!hasCompletedPlanSelection && recommendedPlans.Any(plan => plan.RequiresStripeSubscription))
        {
            steps.Add(new OnboardingStepPageItem(
                "choose_plan",
                "Choose plan",
                "Stay free or choose a paid tier. Paid upgrades complete after secure Stripe checkout.",
                false,
                hasCompletedPlanSelection));
        }

        return new OnboardingPageModel
        {
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName,
            LastName = user.LastName,
            EmailConfirmed = user.EmailConfirmed,
            PhoneNumber = user.PhoneNumber,
            PhoneNumberConfirmed = user.PhoneNumberConfirmed,
            SmsConsentAccepted = user.SmsConsentAccepted,
            AccountTypes = roles.ToList(),
            AvailableAccountTypes = GetAccountTypeOptions(roles),
            RecommendedPlans = recommendedPlans,
            ShowRecommendedPlans = !hasCompletedPlanSelection,
            LaunchPromotion = launchPromotion,
            FeatureCodes = entitlements?.FeatureCodes ?? [],
            ShowTryoutRegistrationList = hasPlayerOrParentRole,
            UpcomingTryoutRegistrations = upcomingTryoutRegistrations,
            FavoritePlayerListings = favoritePlayerListings,
            FavoriteOpportunities = favoriteOpportunities,
            RecentActivity = recentActivity,
            Steps = steps
        };
    }

    private async Task<CoachGettingStartedPageModel> BuildCoachGettingStartedPageModelAsync(
        User user,
        CancellationToken cancellationToken)
    {
        var roles = await GetCanonicalPublicRolesAsync(user);
        var entitlements = await entitlementService.GetEntitlementsAsync(user.Id, cancellationToken);
        var activePlanCodes = entitlements?.ActivePlanCodes ?? [];
        var hasTeamRepresentativeRole = roles.Any(TryOutSpotRoles.IsTeamBundleRole);
        var hasTeamBasicOrHigherPlan = HasActiveTeamBasicOrHigherPlan(activePlanCodes);
        var canConfigureTryoutRegistration = await CanConfigureTryoutRegistrationAsync(user.Id, cancellationToken);

        var teamDashboard = hasTeamRepresentativeRole
            ? await BuildTeamOpportunityDashboardPageModelAsync(user, cancellationToken)
            : null;
        var teams = teamDashboard?.Teams.OrderBy(team => team.TeamName).ToArray()
            ?? [];
        var primaryTeam = teams.FirstOrDefault();
        var teamIds = teams.Select(team => team.TeamId).ToArray();

        var latestTryout = await GetLatestCoachGettingStartedOpportunityAsync(
            teamIds,
            "tryout",
            cancellationToken);
        var latestPickupPlayerListing = await GetLatestCoachGettingStartedOpportunityAsync(
            teamIds,
            "pickup_player",
            cancellationToken);

        var teamPlanLabel = ResolveCoachTeamPlanLabel(activePlanCodes);
        var canPostOpportunities = teamDashboard?.CanPostOpportunities == true;
        var steps = BuildCoachGettingStartedSteps(
            roles,
            hasTeamRepresentativeRole,
            hasTeamBasicOrHigherPlan,
            canPostOpportunities,
            canConfigureTryoutRegistration,
            primaryTeam,
            latestTryout,
            latestPickupPlayerListing);

        return new CoachGettingStartedPageModel
        {
            FirstName = user.FirstName,
            HasTeamRepresentativeRole = hasTeamRepresentativeRole,
            HasTeamBasicOrHigherPlan = hasTeamBasicOrHigherPlan,
            CanPostOpportunities = canPostOpportunities,
            CanConfigureTryoutRegistration = canConfigureTryoutRegistration,
            TeamPlanLabel = teamPlanLabel,
            PrimaryTeam = primaryTeam,
            LatestTryout = latestTryout,
            LatestPickupPlayerListing = latestPickupPlayerListing,
            Teams = teams,
            Steps = steps
        };
    }

    private async Task<CoachGettingStartedOpportunityPageItem?> GetLatestCoachGettingStartedOpportunityAsync(
        IReadOnlyCollection<Guid> teamIds,
        string type,
        CancellationToken cancellationToken)
    {
        if (teamIds.Count == 0)
        {
            return null;
        }

        var opportunity = await dbContext.Opportunities
            .AsNoTracking()
            .Where(currentOpportunity => teamIds.Contains(currentOpportunity.TeamId))
            .Where(currentOpportunity => currentOpportunity.IsActive)
            .Where(currentOpportunity => currentOpportunity.Type == type)
            .OrderByDescending(currentOpportunity => currentOpportunity.UpdatedAt)
            .Select(currentOpportunity => new
            {
                currentOpportunity.TeamId,
                OpportunityId = currentOpportunity.Id,
                currentOpportunity.Title,
                currentOpportunity.Type,
                currentOpportunity.IsPublished,
                currentOpportunity.RegistrationRequired,
                HasPdfFlyer = currentOpportunity.PdfUrl != null
                    && currentOpportunity.PdfUrl != string.Empty
                    || currentOpportunity.UploadedPdfObjectKey != null
                    && currentOpportunity.UploadedPdfObjectKey != string.Empty,
                RegistrationCount = currentOpportunity.Registrations.Count,
                currentOpportunity.UpdatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        return opportunity is null
            ? null
            : new CoachGettingStartedOpportunityPageItem(
                opportunity.TeamId,
                opportunity.OpportunityId,
                opportunity.Title,
                opportunity.Type,
                opportunity.IsPublished,
                opportunity.RegistrationRequired,
                opportunity.HasPdfFlyer,
                opportunity.RegistrationCount,
                opportunity.UpdatedAt);
    }

    private static IReadOnlyCollection<CoachGettingStartedStepPageItem> BuildCoachGettingStartedSteps(
        IReadOnlyCollection<string> roles,
        bool hasTeamRepresentativeRole,
        bool hasTeamBasicOrHigherPlan,
        bool canPostOpportunities,
        bool canConfigureTryoutRegistration,
        ManagedTeamOpportunitySummaryPageModel? primaryTeam,
        CoachGettingStartedOpportunityPageItem? latestTryout,
        CoachGettingStartedOpportunityPageItem? latestPickupPlayerListing)
    {
        var accountTypeUrl = roles.Count == 0
            ? "/account/onboarding#onboarding-account-types"
            : "/account/settings";
        var hasTeamProfile = primaryTeam is not null;
        var foundationComplete = hasTeamRepresentativeRole
            && hasTeamBasicOrHigherPlan
            && hasTeamProfile;
        var createTryoutUrl = primaryTeam is null
            ? null
            : BuildCreateTeamOpportunityPath(primaryTeam.TeamId, "tryout");
        var createPickupUrl = primaryTeam is null
            ? null
            : BuildCreateTeamOpportunityPath(primaryTeam.TeamId, "pickup_player");
        var tryoutEditUrl = latestTryout is null
            ? null
            : BuildEditTeamOpportunityPath(latestTryout.TeamId, latestTryout.OpportunityId);
        var pickupEditUrl = latestPickupPlayerListing is null
            ? null
            : BuildEditTeamOpportunityPath(
                latestPickupPlayerListing.TeamId,
                latestPickupPlayerListing.OpportunityId);
        var teamWorkspaceUrl = primaryTeam is null
            ? "/account/onboarding/team-opportunities"
            : BuildTeamOpportunitiesPath(primaryTeam.TeamId);

        var steps = new List<CoachGettingStartedStepPageItem>
        {
            BuildCoachFoundationStep(
                accountTypeUrl,
                hasTeamRepresentativeRole,
                hasTeamBasicOrHigherPlan,
                primaryTeam,
                teamWorkspaceUrl),
            BuildCoachTryoutListingStep(
                foundationComplete,
                canPostOpportunities,
                latestTryout,
                createTryoutUrl,
                tryoutEditUrl,
                teamWorkspaceUrl),
            BuildCoachTryoutDayStep(
                foundationComplete,
                canConfigureTryoutRegistration,
                latestTryout,
                teamWorkspaceUrl),
            BuildCoachPickupPlayerStep(
                foundationComplete,
                canPostOpportunities,
                latestPickupPlayerListing,
                createPickupUrl,
                pickupEditUrl,
                teamWorkspaceUrl)
        };

        return steps;
    }

    private static CoachGettingStartedStepPageItem BuildCoachFoundationStep(
        string accountTypeUrl,
        bool hasTeamRepresentativeRole,
        bool hasTeamBasicOrHigherPlan,
        ManagedTeamOpportunitySummaryPageModel? primaryTeam,
        string teamWorkspaceUrl)
    {
        var hasTeamProfile = primaryTeam is not null;
        var isComplete = hasTeamRepresentativeRole && hasTeamBasicOrHigherPlan && hasTeamProfile;
        string actionLabel;
        string actionUrl;

        if (!hasTeamRepresentativeRole)
        {
            actionLabel = "Choose team role";
            actionUrl = accountTypeUrl;
        }
        else if (!hasTeamBasicOrHigherPlan)
        {
            actionLabel = "Choose Basic Team";
            actionUrl = "/account/onboarding/choose-plan";
        }
        else if (!hasTeamProfile)
        {
            actionLabel = "Add team profile";
            actionUrl = "/account/onboarding/add-team-or-organization";
        }
        else
        {
            actionLabel = "Open team workspace";
            actionUrl = teamWorkspaceUrl;
        }

        var details = new[]
        {
            hasTeamRepresentativeRole
                ? "Team representative role is selected."
                : "Select Team representative so coach tools appear.",
            hasTeamBasicOrHigherPlan
                ? "Basic Team or higher is active for registration tools."
                : "Choose Basic Team or higher before turning on tryout registration.",
            hasTeamProfile
                ? $"Team profile ready: {primaryTeam!.TeamName}."
                : "Add the team profile players will see on listings."
        };

        return new CoachGettingStartedStepPageItem(
            1,
            "Start the coach account",
            "Set the account role, plan, and team profile before creating listings.",
            isComplete ? "Complete" : "Next",
            isComplete,
            true,
            actionLabel,
            actionUrl,
            details,
            []);
    }

    private static CoachGettingStartedStepPageItem BuildCoachTryoutListingStep(
        bool foundationComplete,
        bool canPostOpportunities,
        CoachGettingStartedOpportunityPageItem? latestTryout,
        string? createTryoutUrl,
        string? tryoutEditUrl,
        string teamWorkspaceUrl)
    {
        var isAvailable = foundationComplete && canPostOpportunities;
        var isComplete = latestTryout is not null
            && latestTryout.IsPublished
            && latestTryout.RegistrationRequired
            && latestTryout.HasPdfFlyer;
        var actionUrl = latestTryout is null ? createTryoutUrl : tryoutEditUrl;
        var actionLabel = latestTryout is null ? "Create tryout listing" : "Edit tryout listing";
        string[] details = latestTryout is null
            ?
            [
                "Create the tryout listing with age group, dates, location, and contact details.",
                "Add a flyer and enable registration from the same listing form.",
                "Publish when the listing is ready for players."
            ]
            :
            [
                $"Latest tryout: {latestTryout.Title}.",
                latestTryout.HasPdfFlyer ? "Flyer is attached." : "Flyer still needs to be added.",
                latestTryout.RegistrationRequired ? "Registration is turned on." : "Registration still needs to be turned on.",
                latestTryout.IsPublished ? "Listing is published." : "Listing is still unpublished."
            ];
        CoachGettingStartedStepLinkPageItem[] secondaryLinks = latestTryout is null
            ? [new CoachGettingStartedStepLinkPageItem("Open team listings", teamWorkspaceUrl, isAvailable)]
            : new[]
            {
                new CoachGettingStartedStepLinkPageItem("Open team listings", teamWorkspaceUrl),
                new CoachGettingStartedStepLinkPageItem(
                    "View public page",
                    $"/opportunities/{latestTryout.OpportunityId}",
                    latestTryout.IsPublished)
            };

        return new CoachGettingStartedStepPageItem(
            2,
            "Post a tryout listing",
            "Create the public tryout page, attach the flyer, turn on registration, and publish.",
            isComplete ? "Complete" : isAvailable ? "Next" : "Locked",
            isComplete,
            isAvailable,
            actionLabel,
            actionUrl,
            details,
            secondaryLinks);
    }

    private static CoachGettingStartedStepPageItem BuildCoachTryoutDayStep(
        bool foundationComplete,
        bool canConfigureTryoutRegistration,
        CoachGettingStartedOpportunityPageItem? latestTryout,
        string teamWorkspaceUrl)
    {
        var isAvailable = foundationComplete && latestTryout is not null;
        var checkInUrl = latestTryout is null
            ? null
            : BuildTeamOpportunityRegistrationPath(latestTryout.TeamId, latestTryout.OpportunityId, "check-in");
        var evaluationUrl = latestTryout is null
            ? null
            : BuildTeamOpportunityRegistrationPath(latestTryout.TeamId, latestTryout.OpportunityId, "evaluation");
        string[] details = latestTryout is null
            ?
            [
                "Create a tryout listing first.",
                "Printable sheets become available from the tryout listing workspace."
            ]
            :
            [
                $"{latestTryout.RegistrationCount} player registration(s) are on the latest tryout.",
                canConfigureTryoutRegistration
                    ? "Check-in and coach evaluation sheets are ready to print."
                    : "Upgrade to Basic Team or higher before collecting registrations."
            ];
        CoachGettingStartedStepLinkPageItem[] secondaryLinks = evaluationUrl is null
            ? []
            : new[]
            {
                new CoachGettingStartedStepLinkPageItem("Print coach sheet", evaluationUrl),
                new CoachGettingStartedStepLinkPageItem("Open registrations", teamWorkspaceUrl)
            };

        return new CoachGettingStartedStepPageItem(
            3,
            "Run tryout day",
            "Use printable check-in and coach evaluation sheets when players arrive.",
            isAvailable ? "Ready" : "Locked",
            false,
            isAvailable,
            "Print check-in sheet",
            checkInUrl,
            details,
            secondaryLinks);
    }

    private static CoachGettingStartedStepPageItem BuildCoachPickupPlayerStep(
        bool foundationComplete,
        bool canPostOpportunities,
        CoachGettingStartedOpportunityPageItem? latestPickupPlayerListing,
        string? createPickupUrl,
        string? pickupEditUrl,
        string teamWorkspaceUrl)
    {
        var isAvailable = foundationComplete && canPostOpportunities;
        var isComplete = latestPickupPlayerListing?.IsPublished == true;
        var actionUrl = latestPickupPlayerListing is null ? createPickupUrl : pickupEditUrl;
        var actionLabel = latestPickupPlayerListing is null ? "Create pickup listing" : "Edit pickup listing";
        string[] details = latestPickupPlayerListing is null
            ?
            [
                "Create a pickup player opportunity when the roster needs short-term help.",
                "Use the same team listing workspace and select Pickup player as the type."
            ]
            :
            [
                $"Latest pickup listing: {latestPickupPlayerListing.Title}.",
                latestPickupPlayerListing.IsPublished ? "Pickup listing is published." : "Pickup listing is still unpublished."
            ];

        return new CoachGettingStartedStepPageItem(
            4,
            "Post a pickup player listing",
            "Create a short-term roster need after the team and tryout workflow are in place.",
            isComplete ? "Complete" : isAvailable ? "Next" : "Locked",
            isComplete,
            isAvailable,
            actionLabel,
            actionUrl,
            details,
            [new CoachGettingStartedStepLinkPageItem("Open team listings", teamWorkspaceUrl, isAvailable)]);
    }

    private static bool HasActiveTeamBasicOrHigherPlan(IReadOnlyCollection<string> activePlanCodes)
    {
        return activePlanCodes.Contains(TryOutSpotPlanCodes.TeamBasic, StringComparer.Ordinal)
            || activePlanCodes.Contains(TryOutSpotPlanCodes.TeamProfessional, StringComparer.Ordinal)
            || activePlanCodes.Contains(TryOutSpotPlanCodes.EnterpriseOrganization, StringComparer.Ordinal);
    }

    private static string ResolveCoachTeamPlanLabel(IReadOnlyCollection<string> activePlanCodes)
    {
        if (activePlanCodes.Contains(TryOutSpotPlanCodes.EnterpriseOrganization, StringComparer.Ordinal))
        {
            return "Enterprise Organization";
        }

        if (activePlanCodes.Contains(TryOutSpotPlanCodes.TeamProfessional, StringComparer.Ordinal))
        {
            return "Professional Team";
        }

        if (activePlanCodes.Contains(TryOutSpotPlanCodes.TeamBasic, StringComparer.Ordinal))
        {
            return "Basic Team";
        }

        return activePlanCodes.Contains(TryOutSpotPlanCodes.FreeCoach, StringComparer.Ordinal)
            ? "Free Coach"
            : "No team plan";
    }

    private static string BuildTeamOpportunitiesPath(Guid teamId)
    {
        return $"/account/onboarding/team-opportunities/{teamId}";
    }

    private static string BuildCreateTeamOpportunityPath(Guid teamId, string type)
    {
        return $"/account/onboarding/team-opportunities/{teamId}/new?type={type}";
    }

    private static string BuildEditTeamOpportunityPath(Guid teamId, Guid opportunityId)
    {
        return $"/account/onboarding/team-opportunities/{teamId}/{opportunityId}/edit";
    }

    private static string BuildTeamOpportunityRegistrationPath(
        Guid teamId,
        Guid opportunityId,
        string sheetType)
    {
        return $"/account/onboarding/team-opportunities/{teamId}/{opportunityId}/registrations/{sheetType}";
    }

    private async Task<LaunchPromotionPageItem?> BuildLaunchPromotionPageItemAsync(
        Guid userId,
        IReadOnlyCollection<string> roles,
        bool emailConfirmed,
        CancellationToken cancellationToken)
    {
        try
        {
            var availability = await launchPromotionStatusService.GetLaunchFounderOfferAvailabilityAsync(
                userId,
                roles,
                cancellationToken);
            return ToLaunchPromotionPageItem(availability, emailConfirmed);
        }
        catch (PostgresException exception) when (IsUndefinedTableException(exception))
        {
            return null;
        }
    }

    private static LaunchPromotionPageItem? ToLaunchPromotionPageItem(
        LaunchPromotionAvailability availability,
        bool emailConfirmed)
    {
        if (!availability.IsEnabled)
        {
            return null;
        }

        if (!availability.IsEligibleForCurrentAccountType && !availability.HasAlreadyClaimed)
        {
            return null;
        }

        if ((availability.IsExhausted || availability.HasActiveAccessForEligiblePlans)
            && !availability.HasAlreadyClaimed)
        {
            return null;
        }

        var planNames = availability.EligiblePlanCodes
            .Select(planCode => TryOutSpotBillingCatalog.GetPlan(planCode)?.Name ?? planCode)
            .ToArray();
        var canClaim = availability.CanClaim && emailConfirmed;

        return new LaunchPromotionPageItem(
            availability.PromotionName,
            availability.GrantMonths,
            availability.RemainingCount,
            canClaim,
            !emailConfirmed && availability.CanClaim,
            availability.HasAlreadyClaimed,
            availability.ExistingClaimEndsAtUtc,
            planNames);
    }

    private static string BuildLaunchPromotionClaimMessage(LaunchPromotionClaimResult result)
    {
        return result.Status switch
        {
            LaunchPromotionClaimResultStatus.Claimed => result.EndsAt is null
                ? "Founder offer applied. Your complimentary access is active."
                : $"Founder offer applied. Your complimentary access is active through {result.EndsAt.Value.ToLocalTime():MMM d, yyyy}.",
            LaunchPromotionClaimResultStatus.AlreadyClaimed => "Founder offer already claimed for this account.",
            LaunchPromotionClaimResultStatus.NoEligibleAccountType => "Choose a parent/player or team representative account type before claiming the founder offer.",
            LaunchPromotionClaimResultStatus.PromotionDisabled => "The founder offer is not active right now.",
            LaunchPromotionClaimResultStatus.PromotionExhausted => "The founder offer has no claims remaining.",
            LaunchPromotionClaimResultStatus.AlreadyHasAccess => "Your account already has active access for the founder offer plans.",
            _ => "The founder offer could not be applied. Please try again."
        };
    }

    private async Task<FavoritesPageModel> BuildFavoritesPageModelAsync(
        User user,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var normalizedPageSize = NormalizeFavoritePageSize(pageSize);
        var normalizedPage = Math.Max(1, page);
        var model = new FavoritesPageModel
        {
            FirstName = user.FirstName,
            LastName = user.LastName,
            CurrentPage = normalizedPage,
            PageSize = normalizedPageSize,
            PageSizeOptions = [10, 25, 50, 100]
        };

        try
        {
            var favoriteQuery = dbContext.UserFavorites
                .AsNoTracking()
                .Where(favorite => favorite.UserId == user.Id);
            model.FavoriteCount = await favoriteQuery.CountAsync(cancellationToken);
            model.PlayerListingFavoriteCount = await favoriteQuery
                .CountAsync(favorite => favorite.PlayerListingId != null, cancellationToken);
            model.OpportunityFavoriteCount = await favoriteQuery
                .CountAsync(favorite => favorite.OpportunityId != null, cancellationToken);
            model.TotalPages = Math.Max(1, (int)Math.Ceiling(model.FavoriteCount / (double)normalizedPageSize));
            model.CurrentPage = Math.Min(normalizedPage, model.TotalPages);

            var now = DateTime.UtcNow;
            var skip = (model.CurrentPage - 1) * normalizedPageSize;
            var favorites = await favoriteQuery
                .Include(favorite => favorite.PlayerListing)
                    .ThenInclude(listing => listing!.Player)
                .Include(favorite => favorite.PlayerListing)
                    .ThenInclude(listing => listing!.Sport)
                .Include(favorite => favorite.Opportunity)
                    .ThenInclude(opportunity => opportunity!.Team)
                        .ThenInclude(team => team.Organization)
                .Include(favorite => favorite.Opportunity)
                    .ThenInclude(opportunity => opportunity!.Sport)
                .OrderByDescending(favorite => favorite.CreatedAt)
                .Skip(skip)
                .Take(normalizedPageSize)
                .ToArrayAsync(cancellationToken);

            model.Favorites = favorites
                .Select(favorite => BuildFavoriteListPageItem(favorite, now))
                .Where(favorite => favorite is not null)
                .Cast<FavoriteListPageItem>()
                .ToArray();
        }
        catch (PostgresException exception) when (IsUndefinedTableException(exception))
        {
            model.FavoriteCount = 0;
            model.PlayerListingFavoriteCount = 0;
            model.OpportunityFavoriteCount = 0;
            model.TotalPages = 1;
            model.CurrentPage = 1;
            model.Favorites = [];
        }

        return model;
    }

    private static int NormalizeFavoritePageSize(int pageSize)
    {
        return pageSize switch
        {
            10 or 25 or 50 or 100 => pageSize,
            _ => DefaultFavoritePageSize
        };
    }

    private FavoriteListPageItem? BuildFavoriteListPageItem(UserFavorite favorite, DateTime now)
    {
        if (favorite.Opportunity is not null)
        {
            var opportunity = favorite.Opportunity;
            var effectiveEndDate = opportunity.ListingEndDate ?? opportunity.ExpiresAt;
            var isAvailable = opportunity.IsActive
                && opportunity.IsPublished
                && (effectiveEndDate == null || effectiveEndDate > now)
                && (opportunity.ListingStartDate == null || opportunity.ListingStartDate <= now)
                && opportunity.Team.IsActive
                && opportunity.Team.IsSearchable;

            return new FavoriteListPageItem(
                opportunity.Id,
                true,
                "Opportunity",
                GetOpportunityTypeLabel(opportunity.Type),
                opportunity.Title,
                opportunity.Team.Name,
                opportunity.Team.Organization?.Name,
                opportunity.Sport.Name,
                opportunity.EventDate,
                opportunity.City ?? opportunity.Team.City,
                opportunity.State ?? opportunity.Team.State,
                opportunity.ZipCode ?? opportunity.Team.ZipCode,
                isAvailable,
                favorite.CreatedAt);
        }

        if (favorite.PlayerListing is not null)
        {
            var listing = favorite.PlayerListing;
            var isAvailable = listing.IsActive
                && listing.IsPublished
                && listing.IsSearchable
                && (listing.ExpiresAt == null || listing.ExpiresAt > now);

            return new FavoriteListPageItem(
                listing.Id,
                false,
                "Player",
                GetPlayerListingTypeLabel(listing.ListingType),
                listing.Title,
                listing.Player is null ? null : $"{listing.Player.FirstName} {listing.Player.LastName}".Trim(),
                null,
                listing.Sport?.Name,
                null,
                listing.City,
                listing.State,
                listing.ZipCode,
                isAvailable,
                favorite.CreatedAt);
        }

        return null;
    }

    private async Task<IReadOnlyCollection<OnboardingTryoutRegistrationPageItem>> GetOnboardingTryoutRegistrationsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var cutoffDate = now.Date.AddDays(-1);
        var registrationRows = await dbContext.Registrations
            .AsNoTracking()
            .Where(registration => registration.Player.IsActive)
            .Where(registration =>
                registration.RegisteredByUserId == userId
                || registration.Player.UserPlayerRelationships.Any(relationship =>
                    relationship.UserId == userId
                    && relationship.CanManage))
            .Where(registration => registration.Opportunity.IsActive)
            .Where(registration => registration.Opportunity.IsPublished)
            .Where(registration => registration.Opportunity.Type.ToLower() == "tryout")
            .Select(registration => new
            {
                registration.Id,
                registration.OpportunityId,
                registration.PlayerId,
                PlayerName = $"{registration.Player.FirstName} {registration.Player.LastName}",
                OpportunityTitle = registration.Opportunity.Title,
                TeamName = registration.Opportunity.Team.Name,
                SportName = registration.Opportunity.Sport.Name,
                registration.Status,
                registration.CreatedAt,
                registration.Opportunity.EventDate,
                registration.Opportunity.EventEndDate,
                registration.Opportunity.RegistrationDeadline,
                registration.Opportunity.City,
                registration.Opportunity.State,
                registration.Opportunity.ExpiresAt
            })
            .ToArrayAsync(cancellationToken);

        return registrationRows
            .Where(registrationRow =>
                ClassifyRegistrationStatus(registrationRow.Status) != RegistrationStatusCategory.Declined)
            .Where(registrationRow =>
                (registrationRow.EventEndDate
                 ?? registrationRow.EventDate
                 ?? registrationRow.RegistrationDeadline
                 ?? registrationRow.ExpiresAt
                 ?? registrationRow.CreatedAt) >= cutoffDate)
            .OrderBy(registrationRow =>
                registrationRow.EventDate
                ?? registrationRow.EventEndDate
                ?? registrationRow.RegistrationDeadline
                ?? registrationRow.CreatedAt)
            .ThenBy(registrationRow => registrationRow.PlayerName)
            .ThenBy(registrationRow => registrationRow.OpportunityTitle)
            .Take(12)
            .Select(registrationRow => new OnboardingTryoutRegistrationPageItem(
                registrationRow.Id,
                registrationRow.OpportunityId,
                registrationRow.PlayerId,
                registrationRow.PlayerName.Trim(),
                registrationRow.OpportunityTitle,
                registrationRow.TeamName,
                registrationRow.SportName,
                FormatRegistrationStatusLabel(registrationRow.Status),
                registrationRow.EventDate,
                registrationRow.EventEndDate,
                registrationRow.RegistrationDeadline,
                registrationRow.CreatedAt,
                registrationRow.City,
                registrationRow.State))
            .ToArray();
    }

    private async Task<IReadOnlyCollection<DashboardPlayerListingFavoritePageItem>> GetDashboardPlayerListingFavoritesAsync(
        Guid userId,
        int take,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;
            var normalizedTake = Math.Clamp(take, 1, FavoriteListLimit);
            var favorites = await dbContext.UserFavorites
                .AsNoTracking()
                .Where(favorite => favorite.UserId == userId)
                .Where(favorite => favorite.PlayerListingId != null)
                .Include(favorite => favorite.PlayerListing)
                    .ThenInclude(listing => listing!.Player)
                .Include(favorite => favorite.PlayerListing)
                    .ThenInclude(listing => listing!.Sport)
                .OrderByDescending(favorite => favorite.CreatedAt)
                .Take(normalizedTake)
                .ToArrayAsync(cancellationToken);

            return favorites
                .Where(favorite => favorite.PlayerListing is not null)
                .Select(favorite =>
                {
                    var listing = favorite.PlayerListing!;
                    var isAvailable = listing.IsActive
                        && listing.IsPublished
                        && listing.IsSearchable
                        && (listing.ExpiresAt == null || listing.ExpiresAt > now);
                    return new DashboardPlayerListingFavoritePageItem(
                        listing.Id,
                        GetPlayerListingTypeLabel(listing.ListingType),
                        listing.Title,
                        listing.Player is null ? null : $"{listing.Player.FirstName} {listing.Player.LastName}".Trim(),
                        listing.Sport?.Name,
                        listing.City,
                        listing.State,
                        listing.ZipCode,
                        isAvailable,
                        favorite.CreatedAt);
                })
                .ToArray();
        }
        catch (PostgresException exception) when (IsUndefinedTableException(exception))
        {
            return [];
        }
    }

    private async Task<IReadOnlyCollection<DashboardOpportunityFavoritePageItem>> GetDashboardOpportunityFavoritesAsync(
        Guid userId,
        int take,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;
            var normalizedTake = Math.Clamp(take, 1, FavoriteListLimit);
            var favorites = await dbContext.UserFavorites
                .AsNoTracking()
                .Where(favorite => favorite.UserId == userId)
                .Where(favorite => favorite.OpportunityId != null)
                .Include(favorite => favorite.Opportunity)
                    .ThenInclude(opportunity => opportunity!.Team)
                        .ThenInclude(team => team.Organization)
                .Include(favorite => favorite.Opportunity)
                    .ThenInclude(opportunity => opportunity!.Sport)
                .OrderByDescending(favorite => favorite.CreatedAt)
                .Take(normalizedTake)
                .ToArrayAsync(cancellationToken);

            return favorites
                .Where(favorite => favorite.Opportunity is not null)
                .Select(favorite =>
                {
                    var opportunity = favorite.Opportunity!;
                    var effectiveEndDate = opportunity.ListingEndDate ?? opportunity.ExpiresAt;
                    var isAvailable = opportunity.IsActive
                        && opportunity.IsPublished
                        && (effectiveEndDate == null || effectiveEndDate > now)
                        && (opportunity.ListingStartDate == null || opportunity.ListingStartDate <= now)
                        && opportunity.Team.IsActive
                        && opportunity.Team.IsSearchable;
                    return new DashboardOpportunityFavoritePageItem(
                        opportunity.Id,
                        GetOpportunityTypeLabel(opportunity.Type),
                        opportunity.Title,
                        opportunity.Team.Name,
                        opportunity.Team.Organization?.Name,
                        opportunity.Sport.Name,
                        opportunity.EventDate,
                        opportunity.City ?? opportunity.Team.City,
                        opportunity.State ?? opportunity.Team.State,
                        opportunity.ZipCode ?? opportunity.Team.ZipCode,
                        isAvailable,
                        favorite.CreatedAt);
                })
                .ToArray();
        }
        catch (PostgresException exception) when (IsUndefinedTableException(exception))
        {
            return [];
        }
    }

    private async Task<AddPlayerProfilePageModel> BuildAddPlayerProfilePageModelAsync(
        User user,
        AddPlayerProfilePageModel model,
        CancellationToken cancellationToken)
    {
        var roles = await GetCanonicalPublicRolesAsync(user);
        model.IsParentOrGuardianAccount = HasAnyRole(roles, TryOutSpotRoles.Parent);
        model.IsSelfPlayerAccount = HasAnyRole(roles, TryOutSpotRoles.Player);
        model.EnhancedProfileVisibleToTeams = await entitlementService.HasFeatureAsync(
            user.Id,
            TryOutSpotFeatureCodes.EnhancedPlayerProfile,
            cancellationToken);

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
                    SecondaryPositions = existingDetail.SecondaryPositions,
                    ExperienceLevel = existingDetail.ExperienceLevel,
                    YearsPlaying = existingDetail.YearsPlaying,
                    Availability = existingDetail.Availability
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
        model.CurrentProfileImageUrl = ResolvePlayerProfileImagePublicUrl(model.PlayerId, model.ProfileImageUrl);

        return model;
    }

    private async Task<ManagePlayerProfilesPageModel> BuildManagePlayerProfilesPageModelAsync(
        User user,
        CancellationToken cancellationToken)
    {
        var managedRelationships = await dbContext.UserPlayerRelationships
            .AsNoTracking()
            .Where(relationship => relationship.UserId == user.Id)
            .Where(relationship => relationship.CanManage)
            .Where(relationship => relationship.Player.IsActive)
            .Include(relationship => relationship.Player)
                .ThenInclude(player => player.PlayerSports)
                    .ThenInclude(playerSport => playerSport.Sport)
            .OrderBy(relationship => relationship.Player.LastName)
            .ThenBy(relationship => relationship.Player.FirstName)
            .ToArrayAsync(cancellationToken);

        var playerIds = managedRelationships
            .Select(relationship => relationship.PlayerId)
            .Distinct()
            .ToArray();
        var activeListingCountsByPlayerId = playerIds.Length == 0
            ? new Dictionary<Guid, int>()
            : await dbContext.PlayerListings
                .AsNoTracking()
                .Where(listing => listing.UserId == user.Id)
                .Where(listing => listing.IsActive)
                .Where(listing => listing.PlayerId.HasValue)
                .Where(listing => playerIds.Contains(listing.PlayerId!.Value))
                .GroupBy(listing => listing.PlayerId!.Value)
                .Select(group => new { PlayerId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.PlayerId, item => item.Count, cancellationToken);

        var profiles = managedRelationships
            .GroupBy(relationship => relationship.PlayerId)
            .Select(group =>
            {
                var relationship = group.First();
                var player = relationship.Player;
                var sports = player.PlayerSports
                    .Where(playerSport => playerSport.IsActive)
                    .OrderBy(playerSport => playerSport.Sport.Name)
                    .Select(playerSport =>
                        string.IsNullOrWhiteSpace(playerSport.SkillLevel)
                            ? playerSport.Sport.Name
                            : $"{playerSport.Sport.Name} ({playerSport.SkillLevel})")
                    .ToArray();

                activeListingCountsByPlayerId.TryGetValue(player.Id, out var activeListingCount);

                return new PlayerProfileSummaryPageModel
                {
                    PlayerId = player.Id,
                    FullName = $"{player.FirstName} {player.LastName}".Trim(),
                    DateOfBirth = player.DateOfBirth.Date,
                    Relationship = relationship.Relationship,
                    CanManage = relationship.CanManage,
                    IsSearchable = player.IsSearchable,
                    ContactVisibility = player.ContactVisibility,
                    ContactEmail = player.ContactEmail,
                    ContactPhone = player.ContactPhone,
                    City = player.City,
                    State = player.State,
                    ZipCode = player.ZipCode,
                    Sports = sports,
                    ActiveListingCount = activeListingCount,
                    UpdatedAt = player.UpdatedAt
                };
            })
            .ToArray();

        return new ManagePlayerProfilesPageModel
        {
            Profiles = profiles,
            IsParentOrGuardianAccount = (await GetCanonicalPublicRolesAsync(user))
                .Contains(TryOutSpotRoles.Parent, StringComparer.OrdinalIgnoreCase)
        };
    }

    private async Task<AddPlayerProfilePageModel?> BuildEditablePlayerProfilePageModelAsync(
        Guid userId,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        var relationshipToUser = await dbContext.UserPlayerRelationships
            .AsNoTracking()
            .Where(relationship => relationship.UserId == userId)
            .Where(relationship => relationship.PlayerId == playerId)
            .Where(relationship => relationship.CanManage)
            .Where(relationship => relationship.Player.IsActive)
            .Include(relationship => relationship.Player)
                .ThenInclude(player => player.PlayerSports)
                    .ThenInclude(playerSport => playerSport.Sport)
            .SingleOrDefaultAsync(cancellationToken);
        if (relationshipToUser is null)
        {
            return null;
        }

        var player = relationshipToUser.Player;
        var socialLinks = DeserializeLinkCollection(player.SocialMediaLinks);
        var recruitingLinks = DeserializeLinkCollection(player.RecruitingProfileLinks);

        socialLinks.TryGetValue("facebook", out var facebookPageUrl);
        socialLinks.TryGetValue("x", out var xPageUrl);
        socialLinks.TryGetValue("instagram", out var instagramUrl);
        socialLinks.TryGetValue("youtube", out var youTubeUrl);
        socialLinks.TryGetValue("tiktok", out var tikTokUrl);
        socialLinks.TryGetValue("highlight_video_1", out var highlightVideoUrl1);
        socialLinks.TryGetValue("highlight_video_2", out var highlightVideoUrl2);

        recruitingLinks.TryGetValue("sportsrecruits", out var sportsRecruitsProfileUrl);
        recruitingLinks.TryGetValue("fieldlevel", out var fieldLevelProfileUrl);
        recruitingLinks.TryGetValue("ncsa", out var ncsaProfileUrl);
        recruitingLinks.TryGetValue("other", out var otherRecruitingProfileUrl);

        return new AddPlayerProfilePageModel
        {
            PlayerId = player.Id,
            IsEditMode = true,
            ReturnUrl = Url.Action(nameof(ManagePlayerProfiles)),
            FirstName = player.FirstName,
            LastName = player.LastName,
            DateOfBirth = player.DateOfBirth.Date,
            Relationship = NormalizeRelationship(relationshipToUser.Relationship) ?? relationshipToUser.Relationship,
            CanManage = relationshipToUser.CanManage,
            IsSearchable = player.IsSearchable,
            ContactVisibility = NormalizePlayerContactVisibility(player.ContactVisibility) ?? PlayerContactVisibilityOptions[1],
            ContactEmail = player.ContactEmail,
            ContactPhone = player.ContactPhone,
            ProfileImageUrl = player.ProfileImageUrl,
            CurrentProfileImageUrl = ResolvePlayerProfileImagePublicUrl(player.Id, player.ProfileImageUrl),
            HighlightVideoUrl1 = highlightVideoUrl1,
            HighlightVideoUrl2 = highlightVideoUrl2,
            SchoolName = player.SchoolName,
            CurrentTeamName = player.CurrentTeamName,
            GraduationYear = player.GraduationYear,
            Height = player.Height,
            Weight = player.Weight,
            ThrowsHand = player.ThrowsHand,
            BatsHand = player.BatsHand,
            SixtyYardDash = player.SixtyYardDash,
            HomeToFirstTime = player.HomeToFirstTime,
            ExitVelocity = player.ExitVelocity,
            ThrowingVelocity = player.ThrowingVelocity,
            PitchVelocity = player.PitchVelocity,
            CatcherPopTime = player.CatcherPopTime,
            AdditionalMetrics = player.AdditionalMetrics,
            City = player.City,
            State = player.State,
            ZipCode = player.ZipCode,
            FacebookPageUrl = facebookPageUrl,
            XPageUrl = xPageUrl,
            InstagramUrl = instagramUrl,
            YouTubeUrl = youTubeUrl,
            TikTokUrl = tikTokUrl,
            SportsRecruitsProfileUrl = sportsRecruitsProfileUrl,
            FieldLevelProfileUrl = fieldLevelProfileUrl,
            NcsaProfileUrl = ncsaProfileUrl,
            OtherRecruitingProfileUrl = otherRecruitingProfileUrl,
            SportDetails = player.PlayerSports
                .Where(playerSport => playerSport.IsActive)
                .OrderBy(playerSport => playerSport.Sport.Name)
                .Select(playerSport => new PlayerSportDetailPageModel
                {
                    SportId = playerSport.SportId,
                    SportName = playerSport.Sport.Name,
                    IsSelected = true,
                    SkillLevel = playerSport.SkillLevel,
                    PrimaryPosition = playerSport.PrimaryPosition,
                    SecondaryPositions = playerSport.SecondaryPositions,
                    ExperienceLevel = playerSport.ExperienceLevel,
                    YearsPlaying = playerSport.YearsPlaying,
                    Availability = playerSport.Availability
                })
                .ToList()
        };
    }

    private async Task<PlayerProfileValidationContext?> ValidatePlayerProfileInputAsync(
        AddPlayerProfilePageModel model,
        CancellationToken cancellationToken)
    {
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
        var availableSelectedSports = selectedSportIds.Length == 0
            ? []
            : await dbContext.Sports
                .AsNoTracking()
                .Where(sport => sport.IsActive && selectedSportIds.Contains(sport.Id))
                .Select(sport => sport.Id)
                .ToArrayAsync(cancellationToken);
        if (availableSelectedSports.Length != selectedSportIds.Length)
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

        if (!ModelState.IsValid || string.IsNullOrWhiteSpace(relationship) || contactVisibility is null)
        {
            return null;
        }

        return new PlayerProfileValidationContext(
            relationship,
            contactVisibility,
            availableSelectedSports,
            selectedSportDetails);
    }

    private static void ApplyPlayerProfileValues(
        Player player,
        AddPlayerProfilePageModel model,
        string contactVisibility,
        DateTime now)
    {
        player.FirstName = model.FirstName.Trim();
        player.LastName = model.LastName.Trim();
        player.DateOfBirth = NormalizeUtcDate(model.DateOfBirth);
        player.ContactEmail = NormalizeOptional(model.ContactEmail);
        player.ContactPhone = NormalizeOptional(model.ContactPhone);
        if ((model.ProfileImageUpload is null || model.ProfileImageUpload.Length == 0)
            && !model.RemoveProfileImage)
        {
            player.ProfileImageUrl = NormalizeOptional(model.ProfileImageUrl);
        }
        player.Height = NormalizeOptional(model.Height);
        player.Weight = NormalizeOptional(model.Weight);
        player.ThrowsHand = NormalizeOptional(model.ThrowsHand);
        player.BatsHand = NormalizeOptional(model.BatsHand);
        player.SixtyYardDash = NormalizeOptional(model.SixtyYardDash);
        player.HomeToFirstTime = NormalizeOptional(model.HomeToFirstTime);
        player.ExitVelocity = NormalizeOptional(model.ExitVelocity);
        player.ThrowingVelocity = NormalizeOptional(model.ThrowingVelocity);
        player.PitchVelocity = NormalizeOptional(model.PitchVelocity);
        player.CatcherPopTime = NormalizeOptional(model.CatcherPopTime);
        player.AdditionalMetrics = NormalizeOptional(model.AdditionalMetrics);
        player.SchoolName = NormalizeOptional(model.SchoolName);
        player.CurrentTeamName = NormalizeOptional(model.CurrentTeamName);
        player.GraduationYear = model.GraduationYear;
        player.ContactVisibility = contactVisibility;
        player.City = NormalizeOptional(model.City);
        player.State = NormalizeState(model.State);
        player.ZipCode = NormalizeOptional(model.ZipCode);
        player.SocialMediaLinks = SerializeLinkCollection(
            ("facebook", model.FacebookPageUrl),
            ("x", NormalizeSocialHandleOrUrl(model.XPageUrl, "https://x.com/")),
            ("instagram", NormalizeSocialHandleOrUrl(model.InstagramUrl, "https://instagram.com/")),
            ("youtube", model.YouTubeUrl),
            ("tiktok", NormalizeSocialHandleOrUrl(model.TikTokUrl, "https://tiktok.com/")),
            ("highlight_video_1", model.HighlightVideoUrl1),
            ("highlight_video_2", model.HighlightVideoUrl2));
        player.RecruitingProfileLinks = SerializeLinkCollection(
            ("sportsrecruits", model.SportsRecruitsProfileUrl),
            ("fieldlevel", model.FieldLevelProfileUrl),
            ("ncsa", model.NcsaProfileUrl),
            ("other", model.OtherRecruitingProfileUrl));
        player.IsSearchable = model.IsSearchable;
        player.UpdatedAt = now;
    }

    private static IReadOnlyDictionary<string, string> DeserializeLinkCollection(string? serializedLinks)
    {
        if (string.IsNullOrWhiteSpace(serializedLinks))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        try
        {
            var links = JsonSerializer.Deserialize<Dictionary<string, string>>(serializedLinks);
            if (links is null || links.Count == 0)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            return new Dictionary<string, string>(links, StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private async Task<PlayerListingListPageModel> BuildPlayerListingListPageModelAsync(
        User user,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var managedPlayers = await GetManagedPlayersAsync(user.Id, cancellationToken);
        var query = dbContext.PlayerListings
            .AsNoTracking()
            .Where(listing => listing.UserId == user.Id)
            .Where(listing => listing.IsActive);
        var totalCount = await query.CountAsync(cancellationToken);
        var listings = await query
            .Include(listing => listing.Player)
            .Include(listing => listing.Sport)
            .OrderByDescending(listing => listing.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        return new PlayerListingListPageModel
        {
            CanCreateListings = true,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize),
            ManagedPlayers = managedPlayers,
            Listings = listings.Select(listing =>
            {
                var summary = ToPlayerListingSummaryPageModel(listing);
                if (!string.IsNullOrWhiteSpace(listing.UploadedPdfObjectKey))
                {
                    summary.UploadedPdfUrl = BuildListingDocumentPath(PlayerListingDocumentType, listing.Id);
                    summary.UploadedPdfFileName = listing.UploadedPdfFileName;
                }

                return summary;
            }).ToArray()
        };
    }

    private async Task<PlayerListingEditorPageModel?> BuildPlayerListingEditorPageModelAsync(
        User user,
        Guid? listingId,
        PlayerListingEditorPageModel? model,
        CancellationToken cancellationToken)
    {
        PlayerListing? listing = null;
        if (listingId.HasValue)
        {
            listing = await dbContext.PlayerListings
                .AsNoTracking()
                .Where(currentListing => currentListing.Id == listingId.Value)
                .Where(currentListing => currentListing.UserId == user.Id)
                .Where(currentListing => currentListing.IsActive)
                .SingleOrDefaultAsync(cancellationToken);

            if (listing is null && model is null)
            {
                return null;
            }
        }

        if (listing is not null && model is null)
        {
            model = new PlayerListingEditorPageModel
            {
                ListingId = listing.Id,
                IsEditMode = true,
                ListingType = listing.ListingType,
                Title = listing.Title,
                Description = listing.Description,
                PlayerId = listing.PlayerId,
                SportId = listing.SportId,
                AskingPrice = listing.AskingPrice,
                Currency = listing.Currency,
                Condition = listing.Condition,
                City = listing.City,
                State = listing.State,
                ZipCode = listing.ZipCode,
                VisibleSocialLinkKeys = ParsePlayerListingVisibleSocialKeys(listing.VisibleSocialLinkKeys).ToList(),
                IsSearchable = listing.IsSearchable,
                IsPublished = listing.IsPublished,
                ExpiresAt = listing.ExpiresAt
            };
        }

        if (model is null)
        {
            model = new PlayerListingEditorPageModel
            {
                ListingType = TryOutSpotPlayerListingTypes.PickupPlayer,
                Currency = "USD",
                IsSearchable = false,
                IsPublished = true,
                VisibleSocialLinkKeys = GetDefaultVisibleSocialLinkKeys().ToList(),
                City = user.City,
                State = user.State,
                ZipCode = user.ZipCode
            };
        }

        model.IsEditMode = listingId.HasValue;
        model.ListingId = listingId;
        model.AvailableListingTypes = PlayerListingTypeOptions;
        model.AvailablePlayers = await GetManagedPlayersAsync(user.Id, cancellationToken);
        model.AvailableSports = await dbContext.Sports
            .AsNoTracking()
            .Where(sport => sport.IsActive)
            .OrderBy(sport => sport.Name)
            .Select(sport => new SportSelectionPageItem(
                sport.Id,
                sport.Name,
                model.SportId.HasValue && model.SportId.Value == sport.Id))
            .ToArrayAsync(cancellationToken);
        model.AvailableSocialDisplayOptions = [.. PlayerListingSocialDisplayOptions, .. PlayerListingVideoDisplayOptions];

        var normalizedListingType = NormalizePlayerListingType(model.ListingType, nameof(model.ListingType));
        if (normalizedListingType is not null)
        {
            model.ListingType = normalizedListingType;
        }
        else if (model.AvailableListingTypes.Count > 0)
        {
            model.ListingType = model.AvailableListingTypes.First().Code;
        }

        model.Currency = string.IsNullOrWhiteSpace(model.Currency)
            ? "USD"
            : model.Currency.Trim().ToUpperInvariant();
        model.City = string.IsNullOrWhiteSpace(model.City) ? user.City : model.City;
        model.State = string.IsNullOrWhiteSpace(model.State) ? user.State : model.State;
        model.ZipCode = string.IsNullOrWhiteSpace(model.ZipCode) ? user.ZipCode : model.ZipCode;
        model.VisibleSocialLinkKeys = NormalizePlayerListingVisibleSocialKeys(model.VisibleSocialLinkKeys).ToList();
        model.HasUploadedPdf = listing is not null && !string.IsNullOrWhiteSpace(listing.UploadedPdfObjectKey);
        model.UploadedPdfFileName = listing?.UploadedPdfFileName;
        model.UploadedPdfUrl = listing is null || string.IsNullOrWhiteSpace(listing.UploadedPdfObjectKey)
            ? null
            : BuildListingDocumentPath(PlayerListingDocumentType, listing.Id);

        return model;
    }

    private async Task<PlayerListingValidationContext> ValidatePlayerListingEditorInputAsync(
        User user,
        PlayerListingEditorPageModel model,
        CancellationToken cancellationToken)
    {
        var normalizedListingType = NormalizePlayerListingType(model.ListingType, nameof(model.ListingType));
        var listingTypeOption = normalizedListingType is null
            ? null
            : GetPlayerListingTypeOption(normalizedListingType);
        if (listingTypeOption is null)
        {
            ModelState.AddModelError(
                nameof(model.ListingType),
                "Choose one of the supported listing types.");
            return new PlayerListingValidationContext(null, null);
        }

        if (model.ExpiresAt.HasValue && NormalizeUtc(model.ExpiresAt) <= DateTime.UtcNow)
        {
            ModelState.AddModelError(nameof(model.ExpiresAt), "Expiration date must be in the future.");
        }

        Player? player = null;
        if (model.PlayerId.HasValue)
        {
            player = await dbContext.UserPlayerRelationships
                .AsNoTracking()
                .Where(relationship => relationship.UserId == user.Id)
                .Where(relationship => relationship.PlayerId == model.PlayerId.Value)
                .Where(relationship => relationship.CanManage)
                .Select(relationship => relationship.Player)
                .SingleOrDefaultAsync(cancellationToken);
            if (player is null || !player.IsActive)
            {
                ModelState.AddModelError(nameof(model.PlayerId), "You can only choose players managed by this account.");
            }
        }

        if (listingTypeOption.RequiresPlayerSelection && player is null)
        {
            ModelState.AddModelError(nameof(model.PlayerId), "Choose a player profile for this listing type.");
        }

        if (model.IsSearchable && player is null)
        {
            ModelState.AddModelError(nameof(model.IsSearchable), "Choose a player profile before making this listing searchable.");
        }

        if (model.SportId.HasValue)
        {
            var sportExists = await dbContext.Sports
                .AsNoTracking()
                .AnyAsync(sport => sport.Id == model.SportId.Value && sport.IsActive, cancellationToken);
            if (!sportExists)
            {
                ModelState.AddModelError(nameof(model.SportId), "Selected sport is not available.");
            }
        }
        else if (listingTypeOption.RequiresSportSelection)
        {
            ModelState.AddModelError(nameof(model.SportId), "Choose a sport for this listing type.");
        }

        if (!listingTypeOption.SupportsCondition)
        {
            model.Condition = null;
        }

        if (!listingTypeOption.SupportsAskingPrice)
        {
            model.AskingPrice = null;
            model.Currency = null;
        }
        else
        {
            model.Currency = string.IsNullOrWhiteSpace(model.Currency)
                ? "USD"
                : model.Currency.Trim().ToUpperInvariant();
        }

        return new PlayerListingValidationContext(listingTypeOption, player);
    }

    private async Task<PlayerProfileDetailPageModel> BuildPlayerProfileDetailPageModelAsync(
        Player player,
        User? currentUser,
        CancellationToken cancellationToken)
    {
        var hasEnhancedProfileVisibility = await PlayerHasEnhancedProfileVisibilityAsync(player.Id, cancellationToken);
        var isContactPublic = string.Equals(player.ContactVisibility, "Public", StringComparison.OrdinalIgnoreCase);
        var isCoachOnlyContact = string.Equals(player.ContactVisibility, "VerifiedCoachesOnly", StringComparison.OrdinalIgnoreCase);
        var canViewCoachOnlyContact = await CanViewCoachOnlyPlayerContactAsync(currentUser);
        var canViewContactDetails = isContactPublic || (isCoachOnlyContact && canViewCoachOnlyContact);

        var sports = player.PlayerSports
            .Where(playerSport => playerSport.IsActive)
            .OrderBy(playerSport => playerSport.Sport.Name)
            .Select(playerSport => new PlayerListingSportSummaryPageItem(
                playerSport.Sport.Name,
                hasEnhancedProfileVisibility ? NormalizeOptional(playerSport.SkillLevel) : null,
                hasEnhancedProfileVisibility ? NormalizeOptional(playerSport.PrimaryPosition) : null,
                hasEnhancedProfileVisibility ? NormalizeOptional(playerSport.SecondaryPositions) : null,
                hasEnhancedProfileVisibility ? NormalizeOptional(playerSport.ExperienceLevel) : null,
                hasEnhancedProfileVisibility ? playerSport.YearsPlaying : null,
                hasEnhancedProfileVisibility ? NormalizeOptional(playerSport.Availability) : null))
            .ToArray();

        var socialLinks = hasEnhancedProfileVisibility
            ? BuildPlayerExternalLinkItems(
                player.SocialMediaLinks,
                ("facebook", "Facebook"),
                ("x", "X"),
                ("instagram", "Instagram"),
                ("youtube", "YouTube"),
                ("tiktok", "TikTok"))
            : [];
        var profileVideoLinks = hasEnhancedProfileVisibility
            ? BuildPlayerExternalLinkItems(
                player.SocialMediaLinks,
                ("highlight_video_1", "Highlight video 1"),
                ("highlight_video_2", "Highlight video 2"))
            : [];
        var recruitingLinks = hasEnhancedProfileVisibility
            ? BuildPlayerExternalLinkItems(
                player.RecruitingProfileLinks,
                ("sportsrecruits", "SportsRecruits"),
                ("fieldlevel", "FieldLevel"),
                ("ncsa", "NCSA"),
                ("other", "Other recruiting profile"))
            : [];

        var now = DateTime.UtcNow;
        var listingRows = await dbContext.PlayerListings
            .AsNoTracking()
            .Where(listing => listing.PlayerId == player.Id)
            .Where(listing => listing.IsActive)
            .Where(listing => listing.IsPublished)
            .Where(listing => listing.IsSearchable)
            .Where(listing => listing.ExpiresAt == null || listing.ExpiresAt > now)
            .Include(listing => listing.Sport)
            .OrderByDescending(listing => listing.PublishedAt)
            .ThenByDescending(listing => listing.UpdatedAt)
            .Take(8)
            .ToArrayAsync(cancellationToken);
        var activeListings = listingRows
            .Select(listing => new PlayerProfileListingSummaryPageItem(
                listing.Id,
                GetPlayerListingTypeLabel(listing.ListingType),
                listing.Title,
                NormalizeOptional(listing.Description),
                listing.Sport?.Name,
                listing.AskingPrice,
                listing.Currency,
                listing.City,
                listing.State,
                listing.ZipCode,
                listing.PublishedAt,
                listing.ExpiresAt))
            .ToArray();

        return new PlayerProfileDetailPageModel
        {
            PlayerId = player.Id,
            PlayerName = $"{player.FirstName} {player.LastName}".Trim(),
            ProfileImageUrl = ResolvePlayerProfileImagePublicUrl(player.Id, player.ProfileImageUrl),
            DateOfBirth = player.DateOfBirth.Date,
            City = NormalizeOptional(player.City),
            State = NormalizeState(player.State),
            ZipCode = NormalizeOptional(player.ZipCode),
            SchoolName = NormalizeOptional(player.SchoolName),
            CurrentTeamName = NormalizeOptional(player.CurrentTeamName),
            GraduationYear = player.GraduationYear,
            Height = NormalizeOptional(player.Height),
            Weight = NormalizeOptional(player.Weight),
            ThrowsHand = NormalizeOptional(player.ThrowsHand),
            BatsHand = NormalizeOptional(player.BatsHand),
            SixtyYardDash = hasEnhancedProfileVisibility ? NormalizeOptional(player.SixtyYardDash) : null,
            HomeToFirstTime = hasEnhancedProfileVisibility ? NormalizeOptional(player.HomeToFirstTime) : null,
            ExitVelocity = hasEnhancedProfileVisibility ? NormalizeOptional(player.ExitVelocity) : null,
            ThrowingVelocity = hasEnhancedProfileVisibility ? NormalizeOptional(player.ThrowingVelocity) : null,
            PitchVelocity = hasEnhancedProfileVisibility ? NormalizeOptional(player.PitchVelocity) : null,
            CatcherPopTime = hasEnhancedProfileVisibility ? NormalizeOptional(player.CatcherPopTime) : null,
            AdditionalMetrics = hasEnhancedProfileVisibility ? NormalizeOptional(player.AdditionalMetrics) : null,
            CanViewContactDetails = canViewContactDetails,
            ContactEmail = canViewContactDetails ? NormalizeOptional(player.ContactEmail) : null,
            ContactPhone = canViewContactDetails ? NormalizeOptional(player.ContactPhone) : null,
            Sports = sports,
            SocialLinks = socialLinks,
            ProfileVideoLinks = profileVideoLinks,
            RecruitingLinks = recruitingLinks,
            ActiveListings = activeListings,
            ViewerIsAuthenticated = currentUser is not null
        };
    }

    private async Task<PlayerListingDetailPageModel> BuildPlayerListingDetailPageModelAsync(
        PlayerListing listing,
        User? currentUser,
        CancellationToken cancellationToken)
    {
        var player = listing.Player;
        var hasEnhancedProfileVisibility = await entitlementService.HasFeatureAsync(
            listing.UserId,
            TryOutSpotFeatureCodes.EnhancedPlayerProfile,
            cancellationToken);
        var isContactPublic = player is not null
            && string.Equals(player.ContactVisibility, "Public", StringComparison.OrdinalIgnoreCase);
        var isCoachOnlyContact = player is not null
            && string.Equals(player.ContactVisibility, "VerifiedCoachesOnly", StringComparison.OrdinalIgnoreCase);
        var canViewCoachOnlyContact = false;
        if (currentUser is not null)
        {
            var viewerRoles = await GetCanonicalPublicRolesAsync(currentUser);
            canViewCoachOnlyContact = viewerRoles.Any(TryOutSpotRoles.IsTeamBundleRole);
        }

        var canViewContactDetails = isContactPublic || (isCoachOnlyContact && canViewCoachOnlyContact);
        var sports = player?.PlayerSports
            .Where(playerSport => playerSport.IsActive)
            .OrderBy(playerSport => playerSport.Sport.Name)
            .Select(playerSport => new PlayerListingSportSummaryPageItem(
                playerSport.Sport.Name,
                hasEnhancedProfileVisibility ? NormalizeOptional(playerSport.SkillLevel) : null,
                hasEnhancedProfileVisibility ? NormalizeOptional(playerSport.PrimaryPosition) : null,
                hasEnhancedProfileVisibility ? NormalizeOptional(playerSport.SecondaryPositions) : null,
                hasEnhancedProfileVisibility ? NormalizeOptional(playerSport.ExperienceLevel) : null,
                hasEnhancedProfileVisibility ? playerSport.YearsPlaying : null,
                hasEnhancedProfileVisibility ? NormalizeOptional(playerSport.Availability) : null))
            .ToArray() ?? [];

        var visibleSocialLinkKeys = ParsePlayerListingVisibleSocialKeys(listing.VisibleSocialLinkKeys);
        var socialLinks = hasEnhancedProfileVisibility
            ? BuildPlayerExternalLinkItems(
            player?.SocialMediaLinks,
            visibleSocialLinkKeys,
            ("facebook", "Facebook"),
            ("x", "X"),
            ("instagram", "Instagram"),
            ("youtube", "YouTube"),
            ("tiktok", "TikTok"))
            : [];
        var profileVideoLinks = hasEnhancedProfileVisibility
            ? BuildPlayerExternalLinkItems(
            player?.SocialMediaLinks,
            visibleSocialLinkKeys,
            ("highlight_video_1", "Highlight video 1"),
            ("highlight_video_2", "Highlight video 2"))
            : [];
        var recruitingLinks = hasEnhancedProfileVisibility
            ? BuildPlayerExternalLinkItems(
            player?.RecruitingProfileLinks,
            ("sportsrecruits", "SportsRecruits"),
            ("fieldlevel", "FieldLevel"),
            ("ncsa", "NCSA"),
            ("other", "Other recruiting profile"))
            : [];
        var isFavorited = currentUser is not null
            && await dbContext.UserFavorites
                .AsNoTracking()
                .AnyAsync(
                    favorite => favorite.UserId == currentUser.Id
                        && favorite.PlayerListingId == listing.Id,
                    cancellationToken);
        var viewerOwnsListing = currentUser?.Id == listing.UserId;
        var viewerHasOpenReport = currentUser is not null
            && await dbContext.ListingReports
                .AsNoTracking()
                .AnyAsync(report => report.ReporterUserId == currentUser.Id
                    && report.PlayerListingId == listing.Id
                    && (report.Status == TryOutSpotListingReportStatuses.Pending
                        || report.Status == TryOutSpotListingReportStatuses.InReview), cancellationToken);

        return new PlayerListingDetailPageModel
        {
            ListingId = listing.Id,
            ListingTypeLabel = GetPlayerListingTypeLabel(listing.ListingType),
            Title = listing.Title,
            Description = listing.Description,
            SportName = listing.Sport?.Name,
            AskingPrice = listing.AskingPrice,
            Currency = listing.Currency,
            Condition = listing.Condition,
            City = listing.City,
            State = listing.State,
            ZipCode = listing.ZipCode,
            PublishedAt = listing.PublishedAt,
            ExpiresAt = listing.ExpiresAt,
            PlayerName = player is null ? null : $"{player.FirstName} {player.LastName}".Trim(),
            PlayerId = player?.Id,
            PublicPlayerProfileUrl = player is { IsActive: true, IsSearchable: true }
                ? $"/players/{player.Id}"
                : null,
            ProfileImageUrl = ResolvePlayerProfileImagePublicUrl(player?.Id, player?.ProfileImageUrl),
            PlayerDateOfBirth = player?.DateOfBirth,
            PlayerCity = NormalizeOptional(player?.City),
            PlayerState = NormalizeState(player?.State),
            PlayerZipCode = NormalizeOptional(player?.ZipCode),
            SchoolName = NormalizeOptional(player?.SchoolName),
            CurrentTeamName = NormalizeOptional(player?.CurrentTeamName),
            GraduationYear = player?.GraduationYear,
            Height = NormalizeOptional(player?.Height),
            Weight = NormalizeOptional(player?.Weight),
            ThrowsHand = NormalizeOptional(player?.ThrowsHand),
            BatsHand = NormalizeOptional(player?.BatsHand),
            SixtyYardDash = hasEnhancedProfileVisibility ? NormalizeOptional(player?.SixtyYardDash) : null,
            HomeToFirstTime = hasEnhancedProfileVisibility ? NormalizeOptional(player?.HomeToFirstTime) : null,
            ExitVelocity = hasEnhancedProfileVisibility ? NormalizeOptional(player?.ExitVelocity) : null,
            ThrowingVelocity = hasEnhancedProfileVisibility ? NormalizeOptional(player?.ThrowingVelocity) : null,
            PitchVelocity = hasEnhancedProfileVisibility ? NormalizeOptional(player?.PitchVelocity) : null,
            CatcherPopTime = hasEnhancedProfileVisibility ? NormalizeOptional(player?.CatcherPopTime) : null,
            AdditionalMetrics = hasEnhancedProfileVisibility ? NormalizeOptional(player?.AdditionalMetrics) : null,
            CanViewContactDetails = canViewContactDetails,
            ContactEmail = canViewContactDetails ? NormalizeOptional(player?.ContactEmail) : null,
            ContactPhone = canViewContactDetails ? NormalizeOptional(player?.ContactPhone) : null,
            Sports = sports,
            SocialLinks = socialLinks,
            ProfileVideoLinks = profileVideoLinks,
            RecruitingLinks = recruitingLinks,
            PdfUrl = string.IsNullOrWhiteSpace(listing.UploadedPdfObjectKey)
                ? null
                : BuildListingDocumentPath(PlayerListingDocumentType, listing.Id),
            PdfFileName = listing.UploadedPdfFileName,
            ViewerIsAuthenticated = currentUser is not null,
            IsFavorited = isFavorited,
            ViewerCanReport = currentUser is not null && !viewerOwnsListing && !viewerHasOpenReport,
            ViewerOwnsListing = viewerOwnsListing,
            ViewerHasOpenReport = viewerHasOpenReport
        };
    }

    private async Task<TeamOpportunityDetailPageModel> BuildTeamOpportunityDetailPageModelAsync(
        Opportunity opportunity,
        User? currentUser,
        TeamOpportunityRegistrationInputPageModel? registrationForm,
        CancellationToken cancellationToken)
    {
        var isContactInfoVisible = opportunity.Team.IsContactInfoVisible
            && (opportunity.Team.Organization?.IsContactInfoVisible ?? true);
        var websiteUrl = isContactInfoVisible
            && TryNormalizeAbsoluteLink(opportunity.WebsiteUrl, out var normalizedWebsiteUrl)
                ? normalizedWebsiteUrl
                : null;
        var externalPdfUrl = isContactInfoVisible
            && TryNormalizeAbsoluteLink(opportunity.PdfUrl, out var normalizedPdfUrl)
                ? normalizedPdfUrl
                : null;
        var uploadedFlyerUrl = isContactInfoVisible && !string.IsNullOrWhiteSpace(opportunity.UploadedPdfObjectKey)
            ? BuildListingDocumentPath(OpportunityDocumentType, opportunity.Id)
            : null;
        var pdfUrl = uploadedFlyerUrl ?? externalPdfUrl;
        var isFlyerImage = uploadedFlyerUrl is not null
            ? IsListingFlyerImageReference(opportunity.UploadedPdfFileName)
                || IsListingFlyerImageReference(opportunity.UploadedPdfObjectKey)
            : IsListingFlyerImageReference(externalPdfUrl);
        var requiredRegistrationFieldCodes = opportunity.RegistrationRequired
            ? DeserializeRegistrationFieldCodes(opportunity.RegistrationRequiredFieldCodes)
            : [];
        var waiverRequired = ResolveWaiverRequired(
            opportunity.WaiverRequired,
            requiredRegistrationFieldCodes);
        var waiverMethod = ResolveWaiverMethod(opportunity.WaiverMethod, waiverRequired);
        var waiverReturnByEmail = waiverRequired && opportunity.WaiverReturnByEmail;
        var waiverReturnInPerson = waiverRequired && opportunity.WaiverReturnInPerson;
        var waiverPdfUrl = waiverRequired
            && string.Equals(waiverMethod, TryOutSpotOpportunityWaiverMethods.Downloadable, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(opportunity.WaiverUploadedPdfObjectKey)
                ? BuildListingDocumentPath(OpportunityWaiverDocumentType, opportunity.Id)
                : null;
        var waiverReturnEmail = waiverReturnByEmail
            ? NormalizeOptional(opportunity.ContactEmail)
            : null;
        var requiredRegistrationFieldOptions = TryOutSpotOpportunityRegistrationFields.All
            .Where(field => requiredRegistrationFieldCodes.Contains(field.Code, StringComparer.OrdinalIgnoreCase))
            .Select(field => new OpportunityRegistrationFieldOptionPageItem(
                field.Code,
                field.Label,
                field.Description,
                IsRequired: true))
            .ToArray();

        var activeRegistrationCount = opportunity.RegistrationRequired
            ? await GetActiveOpportunityRegistrationCountAsync(opportunity.Id, cancellationToken)
            : 0;
        var maxParticipants = opportunity.RegistrationRequired && opportunity.MaxParticipants is > 0
            ? opportunity.MaxParticipants
            : (int?)null;
        var remainingRegistrationSpots = maxParticipants.HasValue
            ? (int?)Math.Max(0, maxParticipants.Value - activeRegistrationCount)
            : (int?)null;
        var registrationClosedReason = opportunity.RegistrationRequired
            ? GetRegistrationClosedReason(opportunity, DateTime.UtcNow, activeRegistrationCount)
            : "Registration is not required for this opportunity.";

        var registrationPlayers = Array.Empty<OpportunityRegistrationPlayerOptionPageItem>();
        if (opportunity.RegistrationRequired && currentUser is not null)
        {
            registrationPlayers = await GetOpportunityRegistrationPlayersAsync(
                opportunity.Id,
                currentUser.Id,
                cancellationToken);
        }

        var effectiveRegistrationForm = registrationForm ?? new TeamOpportunityRegistrationInputPageModel();
        if (registrationPlayers.Length > 0
            && effectiveRegistrationForm.PlayerId == Guid.Empty)
        {
            var firstAvailablePlayer = registrationPlayers.FirstOrDefault(player => !player.AlreadyRegistered);
            if (firstAvailablePlayer is not null)
            {
                effectiveRegistrationForm.PlayerId = firstAvailablePlayer.PlayerId;
            }
        }

        var selectedPlayerOption = registrationPlayers.FirstOrDefault(
            player => player.PlayerId == effectiveRegistrationForm.PlayerId);
        ApplySelectedPlayerRegistrationDefaults(effectiveRegistrationForm, selectedPlayerOption);

        var viewerCanSubmitRegistration = opportunity.RegistrationRequired
            && currentUser is not null
            && registrationPlayers.Any(player => !player.AlreadyRegistered)
            && registrationClosedReason is null;
        var isFavorited = currentUser is not null
            && await dbContext.UserFavorites
                .AsNoTracking()
                .AnyAsync(
                    favorite => favorite.UserId == currentUser.Id
                        && favorite.OpportunityId == opportunity.Id,
                    cancellationToken);
        var viewerManagesTeam = currentUser is not null
            && await dbContext.UserTeamRoles
                .AsNoTracking()
                .AnyAsync(teamRole => teamRole.UserId == currentUser.Id
                    && teamRole.TeamId == opportunity.TeamId
                    && teamRole.IsActive, cancellationToken);
        var viewerHasOpenReport = currentUser is not null
            && await dbContext.ListingReports
                .AsNoTracking()
                .AnyAsync(report => report.ReporterUserId == currentUser.Id
                    && report.OpportunityId == opportunity.Id
                    && (report.Status == TryOutSpotListingReportStatuses.Pending
                        || report.Status == TryOutSpotListingReportStatuses.InReview), cancellationToken);

        if (opportunity.RegistrationRequired && currentUser is null)
        {
            registrationClosedReason ??= "Sign in as a parent or player to register.";
        }
        else if (opportunity.RegistrationRequired && currentUser is not null && registrationPlayers.Length == 0)
        {
            registrationClosedReason ??= "Add a player profile you can manage to submit registration.";
        }
        else if (opportunity.RegistrationRequired && currentUser is not null && !registrationPlayers.Any(player => !player.AlreadyRegistered))
        {
            registrationClosedReason ??= "All of your managed players are already registered for this tryout.";
        }

        return new TeamOpportunityDetailPageModel
        {
            OpportunityId = opportunity.Id,
            TeamId = opportunity.TeamId,
            TeamName = opportunity.Team.Name,
            TeamLogoImageUrl = ResolveTeamLogoPublicUrl(opportunity.Team.Id, opportunity.Team.LogoImageUrl),
            OrganizationName = opportunity.Team.Organization?.Name,
            TeamDescription = NormalizeOptional(opportunity.Team.Description),
            SportName = opportunity.Sport.Name,
            Type = opportunity.Type,
            Title = opportunity.Title,
            Description = NormalizeOptional(opportunity.Description),
            CompetitionLevel = NormalizeOptional(opportunity.CompetitionLevel),
            AgeGroup = NormalizeOptional(opportunity.AgeGroup),
            RegistrationRequired = opportunity.RegistrationRequired,
            RegistrationFee = opportunity.RegistrationFee,
            MaxParticipants = maxParticipants,
            ActiveRegistrationCount = activeRegistrationCount,
            RemainingRegistrationSpots = remainingRegistrationSpots,
            RegistrationDeadline = opportunity.RegistrationDeadline,
            EventDate = opportunity.EventDate,
            EventEndDate = opportunity.EventEndDate,
            PublishedAt = opportunity.PublishedAt,
            ExpiresAt = opportunity.ExpiresAt,
            Location = NormalizeOptional(opportunity.Location),
            Address = NormalizeOptional(opportunity.Address),
            City = NormalizeOptional(opportunity.City),
            State = NormalizeOptional(opportunity.State),
            ZipCode = NormalizeOptional(opportunity.ZipCode),
            IsContactInfoVisible = isContactInfoVisible,
            ContactEmail = isContactInfoVisible ? NormalizeOptional(opportunity.ContactEmail) : null,
            ContactPhone = isContactInfoVisible ? NormalizeOptional(opportunity.ContactPhone) : null,
            WebsiteUrl = websiteUrl,
            PdfUrl = pdfUrl,
            IsFlyerImage = isFlyerImage,
            RequiredEquipment = NormalizeOptional(opportunity.RequiredEquipment),
            WhatToBring = NormalizeOptional(opportunity.WhatToBring),
            SpecialInstructions = NormalizeOptional(opportunity.SpecialInstructions),
            ViewerIsAuthenticated = currentUser is not null,
            ViewerCanSubmitRegistration = viewerCanSubmitRegistration,
            IsRegistrationOpen = opportunity.RegistrationRequired && registrationClosedReason is null,
            RegistrationClosedReason = registrationClosedReason,
            WaiverRequired = waiverRequired,
            WaiverMethod = waiverMethod,
            WaiverReturnByEmail = waiverReturnByEmail,
            WaiverReturnInPerson = waiverReturnInPerson,
            WaiverPdfUrl = waiverPdfUrl,
            WaiverReturnEmail = waiverReturnEmail,
            RequiredRegistrationFields = requiredRegistrationFieldOptions,
            RegistrationPlayers = registrationPlayers,
            RegistrationForm = effectiveRegistrationForm,
            IsFavorited = isFavorited,
            ViewerCanReport = currentUser is not null && !viewerManagesTeam && !viewerHasOpenReport,
            ViewerManagesTeam = viewerManagesTeam,
            ViewerHasOpenReport = viewerHasOpenReport
        };
    }

    private async Task<bool> CanViewCoachOnlyPlayerContactAsync(User? currentUser)
    {
        if (currentUser is null)
        {
            return false;
        }

        var viewerRoles = await GetCanonicalPublicRolesAsync(currentUser);
        return viewerRoles.Any(TryOutSpotRoles.IsTeamBundleRole);
    }

    private async Task<bool> PlayerHasEnhancedProfileVisibilityAsync(
        Guid playerId,
        CancellationToken cancellationToken)
    {
        var managerUserIds = await dbContext.UserPlayerRelationships
            .AsNoTracking()
            .Where(relationship => relationship.PlayerId == playerId)
            .Where(relationship => relationship.CanManage)
            .Where(relationship => relationship.Player.IsActive)
            .Select(relationship => relationship.UserId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        foreach (var managerUserId in managerUserIds)
        {
            if (await entitlementService.HasFeatureAsync(
                    managerUserId,
                    TryOutSpotFeatureCodes.EnhancedPlayerProfile,
                    cancellationToken))
            {
                return true;
            }
        }

        return false;
    }

    private async Task<ChoosePlanPageModel> BuildChoosePlanPageModelAsync(
        User user,
        ChoosePlanPageModel model,
        CancellationToken cancellationToken)
    {
        var roles = await GetCanonicalPublicRolesAsync(user);
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
        var availableBillingIntervals = selectedPlan is null
            ? [BillingIntervalCodes.Month]
            : TryOutSpotBillingCatalog.GetSupportedBillingIntervals(selectedPlan.Code);

        if (availableBillingIntervals.Count == 0)
        {
            availableBillingIntervals = [BillingIntervalCodes.Month];
        }

        if (!availableBillingIntervals.Contains(model.BillingInterval, StringComparer.Ordinal))
        {
            model.BillingInterval = availableBillingIntervals.Contains(BillingIntervalCodes.Year, StringComparer.Ordinal)
                ? BillingIntervalCodes.Year
                : availableBillingIntervals.First();
        }

        model.AvailableBillingIntervals = availableBillingIntervals;
        model.SelectedPlanRequiresAnnualBilling = selectedPlan is not null
            && TryOutSpotBillingCatalog.RequiresAnnualCommitment(selectedPlan.Code);
        model.CheckoutAvailableForSelection = selectedPlan is not null
            && selectedPlan.RequiresStripeSubscription
            && stripeBillingOptions.IsConfigured
            && TryOutSpotBillingCatalog.IsBillingIntervalSupported(selectedPlan.Code, model.BillingInterval)
            && stripeBillingOptions.GetPriceId(selectedPlan.Code, model.BillingInterval) is not null;

        return model;
    }

    private async Task<AddTeamOrOrganizationPageModel> BuildAddTeamOrOrganizationPageModelAsync(
        User user,
        AddTeamOrOrganizationPageModel model,
        CancellationToken cancellationToken)
    {
        var roles = await GetCanonicalPublicRolesAsync(user);
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
        model.CurrentProfileImageUrl = ResolveTeamLogoPublicUrl(model.TeamId, model.ProfileImageUrl);

        return model;
    }

    private async Task<TeamOpportunityDashboardPageModel> BuildTeamOpportunityDashboardPageModelAsync(
        User user,
        CancellationToken cancellationToken)
    {
        var postingAccess = await ResolveTeamPostingAccessAsync(user.Id, cancellationToken);
        var managedTeams = await GetManagedTeamRoleContextsAsync(user.Id, cancellationToken);
        var teamIds = managedTeams
            .Select(managedTeam => managedTeam.Team.Id)
            .Distinct()
            .ToArray();
        var publishedCountsByTeam = await GetPublishedOpportunityCountsForWindowAsync(
            teamIds,
            postingAccess.PublishingWindowMonths,
            cancellationToken);

        var teams = managedTeams
            .Select(managedTeam =>
            {
                publishedCountsByTeam.TryGetValue(managedTeam.Team.Id, out var publishedInWindowCount);
                return ToManagedTeamOpportunitySummary(managedTeam.Team, managedTeam.Role, publishedInWindowCount);
            })
            .OrderBy(team => team.TeamName)
            .ToArray();

        return new TeamOpportunityDashboardPageModel
        {
            CanPostOpportunities = postingAccess.CanPostOpportunities,
            HasLimitedPosting = postingAccess.HasLimitedPosting,
            HasUnlimitedPosting = postingAccess.HasUnlimitedPosting,
            PublishingLimit = postingAccess.PublishingLimit,
            PublishingWindowMonths = postingAccess.PublishingWindowMonths,
            PublishingPlanLabel = postingAccess.PlanLabel,
            Teams = teams
        };
    }

    private async Task<TeamOpportunityListPageModel?> BuildTeamOpportunityListPageModelAsync(
        User user,
        Guid teamId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var managedTeam = await GetManagedTeamRoleContextAsync(user.Id, teamId, cancellationToken);
        if (managedTeam is null)
        {
            return null;
        }

        var postingAccess = await ResolveTeamPostingAccessAsync(user.Id, cancellationToken);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.Opportunities
            .AsNoTracking()
            .Where(opportunity => opportunity.TeamId == teamId)
            .Where(opportunity => opportunity.IsActive);
        var totalCount = await query.CountAsync(cancellationToken);
        var opportunities = await query
            .Include(opportunity => opportunity.Sport)
            .OrderByDescending(opportunity => opportunity.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        var entitlements = await entitlementService.GetEntitlementsAsync(user.Id, cancellationToken);
        var featureCodes = entitlements?.FeatureCodes ?? [];
        var hasDetailedAnalytics = featureCodes.Contains(TryOutSpotFeatureCodes.DetailedTeamAnalytics, StringComparer.Ordinal);
        var hasBasicAnalytics = hasDetailedAnalytics
            || featureCodes.Contains(TryOutSpotFeatureCodes.BasicTeamAnalytics, StringComparer.Ordinal);

        var registrationStatsByOpportunityId = new Dictionary<Guid, ListingRegistrationStats>();
        var registrationDetailsByOpportunityId = new Dictionary<Guid, TeamOpportunityRegistrantPageItem[]>();
        var favoriteCountsByOpportunityId = new Dictionary<Guid, int>();
        if (opportunities.Length > 0)
        {
            var opportunityIds = opportunities.Select(opportunity => opportunity.Id).ToHashSet();
            favoriteCountsByOpportunityId = await dbContext.UserFavorites
                .AsNoTracking()
                .Where(favorite => favorite.OpportunityId.HasValue)
                .Where(favorite => opportunityIds.Contains(favorite.OpportunityId!.Value))
                .GroupBy(favorite => favorite.OpportunityId!.Value)
                .Select(group => new { OpportunityId = group.Key, Count = group.Count() })
                .ToDictionaryAsync(item => item.OpportunityId, item => item.Count, cancellationToken);

            registrationDetailsByOpportunityId = await BuildTeamOpportunityRegistrantDetailsByOpportunityAsync(
                user.Id,
                opportunityIds,
                cancellationToken);

            if (hasBasicAnalytics)
            {
                registrationStatsByOpportunityId = registrationDetailsByOpportunityId
                    .ToDictionary(
                        pair => pair.Key,
                        pair =>
                        {
                            var pendingCount = 0;
                            var approvedCount = 0;
                            var declinedCount = 0;

                            foreach (var registrant in pair.Value)
                            {
                                switch (ClassifyRegistrationStatus(registrant.StatusCode))
                                {
                                    case RegistrationStatusCategory.Approved:
                                        approvedCount++;
                                        break;
                                    case RegistrationStatusCategory.Declined:
                                        declinedCount++;
                                        break;
                                    default:
                                        pendingCount++;
                                        break;
                                }
                            }

                            return new ListingRegistrationStats(
                                pair.Value.Length,
                                pair.Value.Select(registrant => registrant.PlayerId).Distinct().Count(),
                                pendingCount,
                                approvedCount,
                                declinedCount);
                        });
            }
        }

        var publishedInWindowCount = await GetPublishedOpportunityCountForWindowAsync(
            teamId,
            postingAccess.PublishingWindowMonths,
            cancellationToken);

        var opportunitySummaries = opportunities.Select(opportunity =>
        {
            var summary = ToTeamOpportunitySummary(opportunity);
            if (!string.IsNullOrWhiteSpace(opportunity.UploadedPdfObjectKey))
            {
                summary.UploadedPdfUrl = BuildListingDocumentPath(OpportunityDocumentType, opportunity.Id);
                summary.UploadedPdfFileName = opportunity.UploadedPdfFileName;
            }

            if (registrationStatsByOpportunityId.TryGetValue(opportunity.Id, out var stats))
            {
                summary.RegistrationCount = stats.TotalCount;
                summary.UniqueApplicantCount = stats.UniqueApplicantCount;
                summary.PendingRegistrationCount = stats.PendingCount;
                summary.ApprovedRegistrationCount = stats.ApprovedCount;
                summary.DeclinedRegistrationCount = stats.DeclinedCount;
            }

            if (favoriteCountsByOpportunityId.TryGetValue(opportunity.Id, out var favoriteCount))
            {
                summary.FavoriteCount = favoriteCount;
            }

            if (registrationDetailsByOpportunityId.TryGetValue(opportunity.Id, out var registrants))
            {
                summary.Registrants = registrants;
            }

            summary.ViewToRegistrationConversionRate = CalculateConversionRate(summary.RegistrationCount, summary.ViewCount);
            return summary;
        }).ToArray();

        var pageViewCountTotal = opportunitySummaries.Sum(summary => summary.ViewCount);
        var pageRegistrationCountTotal = opportunitySummaries.Sum(summary => summary.RegistrationCount);
        var pageFavoriteCountTotal = opportunitySummaries.Sum(summary => summary.FavoriteCount);

        return new TeamOpportunityListPageModel
        {
            Team = ToManagedTeamOpportunitySummary(
                managedTeam.Team,
                managedTeam.Role,
                publishedInWindowCount),
            CanPostOpportunities = postingAccess.CanPostOpportunities,
            HasLimitedPosting = postingAccess.HasLimitedPosting,
            HasUnlimitedPosting = postingAccess.HasUnlimitedPosting,
            PublishingLimit = postingAccess.PublishingLimit,
            PublishingWindowMonths = postingAccess.PublishingWindowMonths,
            PublishingPlanLabel = postingAccess.PlanLabel,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalCount == 0 ? 1 : (int)Math.Ceiling(totalCount / (double)pageSize),
            ShowBasicAnalytics = hasBasicAnalytics,
            ShowDetailedAnalytics = hasDetailedAnalytics,
            PageViewCountTotal = pageViewCountTotal,
            PageRegistrationCountTotal = pageRegistrationCountTotal,
            PageFavoriteCountTotal = pageFavoriteCountTotal,
            PageViewToRegistrationConversionRate = CalculateConversionRate(pageRegistrationCountTotal, pageViewCountTotal),
            Opportunities = opportunitySummaries
        };
    }

    private async Task<TeamOpportunityRegistrationSharePageModel?> BuildTeamOpportunityRegistrationSharePageModelAsync(
        User user,
        Guid teamId,
        Guid opportunityId,
        CancellationToken cancellationToken)
    {
        var managedTeam = await GetManagedTeamRoleContextAsync(user.Id, teamId, cancellationToken);
        if (managedTeam is null)
        {
            return null;
        }

        var opportunity = await dbContext.Opportunities
            .AsNoTracking()
            .Include(currentOpportunity => currentOpportunity.Sport)
            .Where(currentOpportunity => currentOpportunity.Id == opportunityId)
            .Where(currentOpportunity => currentOpportunity.TeamId == teamId)
            .Where(currentOpportunity => currentOpportunity.IsActive)
            .SingleOrDefaultAsync(cancellationToken);
        if (opportunity is null)
        {
            return null;
        }

        var registrationDetailsByOpportunityId = await BuildTeamOpportunityRegistrantDetailsByOpportunityAsync(
            user.Id,
            [opportunityId],
            cancellationToken);
        registrationDetailsByOpportunityId.TryGetValue(opportunityId, out var registrants);

        return new TeamOpportunityRegistrationSharePageModel
        {
            TeamId = teamId,
            OpportunityId = opportunityId,
            TeamName = managedTeam.Team.Name,
            OrganizationName = managedTeam.Team.Organization?.Name,
            SportName = opportunity.Sport.Name,
            Type = opportunity.Type,
            Title = opportunity.Title,
            CompetitionLevel = opportunity.CompetitionLevel,
            AgeGroup = opportunity.AgeGroup,
            EventDate = opportunity.EventDate,
            EventEndDate = opportunity.EventEndDate,
            Location = opportunity.Location,
            Address = opportunity.Address,
            City = opportunity.City,
            State = opportunity.State,
            ZipCode = opportunity.ZipCode,
            GeneratedAt = DateTime.UtcNow,
            Registrants = registrants ?? []
        };
    }

    private async Task<Dictionary<Guid, TeamOpportunityRegistrantPageItem[]>> BuildTeamOpportunityRegistrantDetailsByOpportunityAsync(
        Guid viewerUserId,
        IReadOnlyCollection<Guid> opportunityIds,
        CancellationToken cancellationToken)
    {
        if (opportunityIds.Count == 0)
        {
            return [];
        }

        var registrationRows = await dbContext.Registrations
            .AsNoTracking()
            .Where(registration => opportunityIds.Contains(registration.OpportunityId))
            .Select(registration => new
            {
                registration.Id,
                registration.OpportunityId,
                registration.PlayerId,
                registration.Status,
                registration.AttendanceStatus,
                registration.CheckInTime,
                registration.WaiverSigned,
                registration.WaiverSignedAt,
                registration.CreatedAt,
                registration.RegistrationData,
                registration.EmergencyContactName,
                registration.EmergencyContactPhone,
                registration.MedicalInfo,
                registration.Notes,
                registration.Player.FirstName,
                registration.Player.LastName,
                PlayerDateOfBirth = registration.Player.DateOfBirth,
                PlayerSchoolName = registration.Player.SchoolName,
                registration.Player.ContactPhone,
                registration.Player.ContactEmail,
                registration.Opportunity.EventDate
            })
            .ToArrayAsync(cancellationToken);
        if (registrationRows.Length == 0)
        {
            return [];
        }

        var playerIds = registrationRows
            .Select(row => row.PlayerId)
            .Distinct()
            .ToArray();
        var favoritePlayerIds = await dbContext.PlayerListings
            .AsNoTracking()
            .Where(listing => listing.PlayerId.HasValue)
            .Where(listing => playerIds.Contains(listing.PlayerId!.Value))
            .Where(listing => dbContext.UserFavorites.Any(favorite =>
                favorite.UserId == viewerUserId
                && favorite.PlayerListingId == listing.Id))
            .Select(listing => listing.PlayerId!.Value)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var favoritePlayerIdSet = favoritePlayerIds.ToHashSet();

        return registrationRows
            .GroupBy(row => row.OpportunityId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(row => row.CreatedAt)
                    .ThenBy(row => row.LastName)
                    .ThenBy(row => row.FirstName)
                    .ThenBy(row => row.Id)
                    .Select((row, index) =>
                    {
                        var registrationData = ParseRegistrationDataSnapshot(row.RegistrationData);
                        var playerBirthDate = registrationData.PlayerBirthDate ?? row.PlayerDateOfBirth;
                        return new TeamOpportunityRegistrantPageItem(
                            row.Id,
                            row.PlayerId,
                            BuildPlayerDisplayName(row.FirstName, row.LastName),
                            index + 1,
                            CalculatePlayerAge(playerBirthDate, row.EventDate),
                            FirstPopulatedValue(registrationData.PlayerSchool, row.PlayerSchoolName),
                            ToRegistrationStatusCode(row.Status),
                            FormatRegistrationStatusLabel(row.Status),
                            IsRegistrationMarkedPresent(row.AttendanceStatus),
                            row.CheckInTime,
                            row.WaiverSigned,
                            row.WaiverSignedAt,
                            row.CreatedAt,
                            favoritePlayerIdSet.Contains(row.PlayerId),
                            FirstPopulatedValue(registrationData.PlayerPhone, row.ContactPhone),
                            FirstPopulatedValue(registrationData.PlayerEmail, row.ContactEmail),
                            registrationData.GuardianName,
                            registrationData.GuardianEmail,
                            registrationData.GuardianPhone,
                            FirstPopulatedValue(row.EmergencyContactName, registrationData.EmergencyContactName),
                            FirstPopulatedValue(row.EmergencyContactPhone, registrationData.EmergencyContactPhone),
                            FirstPopulatedValue(row.MedicalInfo, registrationData.MedicalInfo),
                            FirstPopulatedValue(row.Notes, registrationData.AdditionalNotes));
                    })
                    .ToArray());
    }

    private static RegistrationDataSnapshot ParseRegistrationDataSnapshot(string? registrationData)
    {
        if (string.IsNullOrWhiteSpace(registrationData))
        {
            return RegistrationDataSnapshot.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(registrationData);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return RegistrationDataSnapshot.Empty;
            }

            return new RegistrationDataSnapshot(
                GetJsonDateProperty(root, "playerBirthDate"),
                GetJsonStringProperty(root, "playerSchool"),
                GetJsonStringProperty(root, "playerPhone"),
                GetJsonStringProperty(root, "playerEmail"),
                GetJsonStringProperty(root, "guardianName"),
                GetJsonStringProperty(root, "guardianEmail"),
                GetJsonStringProperty(root, "guardianPhone"),
                GetJsonStringProperty(root, "emergencyContactName"),
                GetJsonStringProperty(root, "emergencyContactPhone"),
                GetJsonStringProperty(root, "medicalInfo"),
                GetJsonStringProperty(root, "additionalNotes"));
        }
        catch (JsonException)
        {
            return RegistrationDataSnapshot.Empty;
        }
    }

    private static DateTime? GetJsonDateProperty(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var rawValue = NormalizeOptional(value.GetString());
        if (rawValue is null)
        {
            return null;
        }

        return DateTime.TryParse(
            rawValue,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed.Date
            : null;
    }

    private static string? GetJsonStringProperty(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? NormalizeOptional(value.GetString())
            : null;
    }

    private static int? CalculatePlayerAge(DateTime? birthDate, DateTime? eventDate)
    {
        if (!birthDate.HasValue)
        {
            return null;
        }

        var referenceDate = (eventDate ?? DateTime.UtcNow).Date;
        var normalizedBirthDate = birthDate.Value.Date;
        var age = referenceDate.Year - normalizedBirthDate.Year;
        if (normalizedBirthDate > referenceDate.AddYears(-age))
        {
            age--;
        }

        return age < 0 ? null : age;
    }

    private static string? FirstPopulatedValue(params string?[] values)
    {
        foreach (var value in values)
        {
            var normalizedValue = NormalizeOptional(value);
            if (!string.IsNullOrWhiteSpace(normalizedValue))
            {
                return normalizedValue;
            }
        }

        return null;
    }

    private async Task<TeamOpportunityEditorPageModel?> BuildTeamOpportunityEditorPageModelAsync(
        User user,
        Guid teamId,
        Guid? opportunityId,
        TeamOpportunityEditorPageModel? model,
        CancellationToken cancellationToken)
    {
        var managedTeam = await GetManagedTeamRoleContextAsync(user.Id, teamId, cancellationToken);
        if (managedTeam is null)
        {
            return null;
        }

        Opportunity? existingOpportunity = null;
        if (opportunityId is not null)
        {
            existingOpportunity = await dbContext.Opportunities
                .AsNoTracking()
                .Where(opportunity =>
                    opportunity.Id == opportunityId.Value
                    && opportunity.TeamId == teamId
                    && opportunity.IsActive)
                .SingleOrDefaultAsync(cancellationToken);
            if (existingOpportunity is null)
            {
                return null;
            }
        }

        var postingAccess = await ResolveTeamPostingAccessAsync(user.Id, cancellationToken);
        var canConfigureTryoutRegistration = await CanConfigureTryoutRegistrationAsync(user.Id, cancellationToken);
        var publishedInWindowCount = await GetPublishedOpportunityCountForWindowAsync(
            teamId,
            postingAccess.PublishingWindowMonths,
            cancellationToken);

        if (model is null)
        {
            if (existingOpportunity is not null && opportunityId is not null)
            {
                var existingRequiredFieldCodes = DeserializeRegistrationFieldCodes(existingOpportunity.RegistrationRequiredFieldCodes).ToList();
                var existingWaiverRequired = ResolveWaiverRequired(
                    existingOpportunity.WaiverRequired,
                    existingRequiredFieldCodes);
                var existingWaiverMethod = ResolveWaiverMethod(
                    existingOpportunity.WaiverMethod,
                    existingWaiverRequired);
                model = new TeamOpportunityEditorPageModel
                {
                    TeamId = teamId,
                    OpportunityId = opportunityId,
                    IsEditMode = true,
                    Type = existingOpportunity.Type,
                    Title = existingOpportunity.Title,
                    Description = existingOpportunity.Description,
                    SportId = existingOpportunity.SportId,
                    CompetitionLevel = existingOpportunity.CompetitionLevel,
                    AgeGroup = existingOpportunity.AgeGroup,
                    RegistrationFee = existingOpportunity.RegistrationFee,
                    RegistrationRequired = existingOpportunity.RegistrationRequired,
                    LimitRegistrationCapacity = existingOpportunity.MaxParticipants is > 0,
                    MaxParticipants = existingOpportunity.MaxParticipants,
                    RequiredRegistrationFieldCodes = existingRequiredFieldCodes,
                    WaiverRequired = existingWaiverRequired,
                    WaiverMethod = existingWaiverMethod,
                    WaiverReturnByEmail = existingWaiverRequired && existingOpportunity.WaiverReturnByEmail,
                    WaiverReturnInPerson = existingWaiverRequired && existingOpportunity.WaiverReturnInPerson,
                    RegistrationDeadline = existingOpportunity.RegistrationDeadline?.Date,
                    EventDate = existingOpportunity.EventDate?.Date,
                    EventEndDate = existingOpportunity.EventEndDate?.Date,
                    ListingStartDate = existingOpportunity.ListingStartDate?.Date,
                    ListingEndDate = (existingOpportunity.ListingEndDate ?? existingOpportunity.ExpiresAt)?.Date,
                    Location = existingOpportunity.Location,
                    Address = existingOpportunity.Address,
                    City = existingOpportunity.City,
                    State = existingOpportunity.State,
                    ZipCode = existingOpportunity.ZipCode,
                    ContactEmail = existingOpportunity.ContactEmail,
                    ContactPhone = existingOpportunity.ContactPhone,
                    WebsiteUrl = existingOpportunity.WebsiteUrl,
                    PdfUrl = existingOpportunity.PdfUrl,
                    RequiredEquipment = existingOpportunity.RequiredEquipment,
                    WhatToBring = existingOpportunity.WhatToBring,
                    SpecialInstructions = existingOpportunity.SpecialInstructions,
                    IsPublished = existingOpportunity.IsPublished,
                    ExpiresAt = existingOpportunity.ExpiresAt?.Date
                };
            }
            else
            {
                model = new TeamOpportunityEditorPageModel
                {
                    TeamId = teamId,
                    ContactEmail = managedTeam.Team.Email,
                    ContactPhone = managedTeam.Team.PhoneNumber,
                    City = managedTeam.Team.City,
                    State = managedTeam.Team.State,
                    ZipCode = managedTeam.Team.ZipCode,
                    WebsiteUrl = managedTeam.Team.WebsiteUrl,
                    PdfUrl = null,
                    Type = TeamOpportunityTypeOptions[0],
                    RegistrationRequired = canConfigureTryoutRegistration,
                    WaiverMethod = TryOutSpotOpportunityWaiverMethods.AtEvent,
                    WaiverReturnInPerson = true,
                    IsPublished = true
                };
            }
        }

        var teamSportIds = managedTeam.Team.TeamSports
            .Where(teamSport => teamSport.IsActive)
            .Select(teamSport => teamSport.SportId)
            .ToHashSet();
        var availableSports = await dbContext.Sports
            .AsNoTracking()
            .Where(sport => sport.IsActive)
            .Where(sport => teamSportIds.Count == 0 || teamSportIds.Contains(sport.Id))
            .OrderBy(sport => sport.Name)
            .Select(sport => new SportSelectionPageItem(
                sport.Id,
                sport.Name,
                sport.Id == model.SportId))
            .ToArrayAsync(cancellationToken);
        if (availableSports.Length > 0 && model.SportId == Guid.Empty)
        {
            model.SportId = availableSports[0].Id;
        }

        model.TeamId = teamId;
        model.OpportunityId = opportunityId;
        model.IsEditMode = opportunityId is not null;
        model.TeamName = managedTeam.Team.Name;
        model.OrganizationName = managedTeam.Team.Organization?.Name;
        model.CanPostOpportunities = postingAccess.CanPostOpportunities;
        model.CanConfigureTryoutRegistration = canConfigureTryoutRegistration;
        model.HasLimitedPosting = postingAccess.HasLimitedPosting;
        model.HasUnlimitedPosting = postingAccess.HasUnlimitedPosting;
        model.PublishingLimit = postingAccess.PublishingLimit;
        model.PublishingWindowMonths = postingAccess.PublishingWindowMonths;
        model.PublishingPlanLabel = postingAccess.PlanLabel;
        model.PublishedInWindowCount = publishedInWindowCount;
        model.AvailableSports = availableSports;
        model.AvailableOpportunityTypes = TeamOpportunityTypeOptions;
        var normalizedRequiredRegistrationFieldCodes = NormalizeOpportunityRegistrationEditorInput(
            model,
            canConfigureTryoutRegistration);
        model.AvailableRegistrationFieldOptions = TryOutSpotOpportunityRegistrationFields.All
            .Where(field => !string.Equals(field.Code, TryOutSpotOpportunityRegistrationFields.WaiverSignature, StringComparison.OrdinalIgnoreCase))
            .Select(field => new OpportunityRegistrationFieldOptionPageItem(
                field.Code,
                field.Label,
                field.Description,
                normalizedRequiredRegistrationFieldCodes.Contains(field.Code, StringComparer.OrdinalIgnoreCase)))
            .ToArray();
        model.HasUploadedPdf = existingOpportunity is not null
            && !string.IsNullOrWhiteSpace(existingOpportunity.UploadedPdfObjectKey);
        model.UploadedPdfFileName = existingOpportunity?.UploadedPdfFileName;
        model.UploadedPdfUrl = existingOpportunity is null || string.IsNullOrWhiteSpace(existingOpportunity.UploadedPdfObjectKey)
            ? null
            : BuildListingDocumentPath(OpportunityDocumentType, existingOpportunity.Id);
        model.HasUploadedWaiverPdf = existingOpportunity is not null
            && !string.IsNullOrWhiteSpace(existingOpportunity.WaiverUploadedPdfObjectKey);
        model.UploadedWaiverPdfFileName = existingOpportunity?.WaiverUploadedPdfFileName;
        model.UploadedWaiverPdfUrl = existingOpportunity is null || string.IsNullOrWhiteSpace(existingOpportunity.WaiverUploadedPdfObjectKey)
            ? null
            : BuildListingDocumentPath(OpportunityWaiverDocumentType, existingOpportunity.Id);

        return model;
    }

    private async Task ValidateTeamOpportunityEditorInputAsync(
        TeamOpportunityEditorPageModel model,
        Team team,
        bool canConfigureTryoutRegistration,
        IReadOnlyCollection<string> normalizedRequiredRegistrationFieldCodes,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.Type))
        {
            ModelState.AddModelError(nameof(model.Type), "Opportunity type is required.");
        }

        if (!canConfigureTryoutRegistration && model.RegistrationRequired)
        {
            ModelState.AddModelError(
                nameof(model.RegistrationRequired),
                "Tryout registration requires Team Basic or higher.");
        }

        var isTryoutType = string.Equals(
            NormalizeOptional(model.Type),
            "tryout",
            StringComparison.OrdinalIgnoreCase);
        if (model.RegistrationRequired && !isTryoutType)
        {
            ModelState.AddModelError(
                nameof(model.RegistrationRequired),
                "Tryout registration can only be enabled for tryout listings.");
        }

        if (!model.RegistrationRequired && model.RegistrationFee > 0)
        {
            ModelState.AddModelError(
                nameof(model.RegistrationFee),
                "Registration fee can only be set when registration is enabled.");
        }

        if (model.RegistrationRequired && model.LimitRegistrationCapacity && model.MaxParticipants is null)
        {
            ModelState.AddModelError(
                nameof(model.MaxParticipants),
                "Provide a max registration count when registration capacity is limited.");
        }

        if (!model.RegistrationRequired && normalizedRequiredRegistrationFieldCodes.Count > 0)
        {
            ModelState.AddModelError(
                nameof(model.RequiredRegistrationFieldCodes),
                "Required registration fields can only be selected when registration is enabled.");
        }

        if (model.WaiverRequired && !model.RegistrationRequired)
        {
            ModelState.AddModelError(
                nameof(model.WaiverRequired),
                "Waiver settings can only be enabled when tryout registration is enabled.");
        }

        if (model.WaiverRequired
            && !string.Equals(model.WaiverMethod, TryOutSpotOpportunityWaiverMethods.AtEvent, StringComparison.Ordinal)
            && !string.Equals(model.WaiverMethod, TryOutSpotOpportunityWaiverMethods.Downloadable, StringComparison.Ordinal))
        {
            ModelState.AddModelError(
                nameof(model.WaiverMethod),
                "Choose whether waiver forms are handled at the event or provided as a downloadable PDF.");
        }

        var requiresWaiverReturnOption = model.WaiverRequired
            && string.Equals(
                model.WaiverMethod,
                TryOutSpotOpportunityWaiverMethods.Downloadable,
                StringComparison.Ordinal);
        if (requiresWaiverReturnOption && !model.WaiverReturnByEmail && !model.WaiverReturnInPerson)
        {
            ModelState.AddModelError(
                nameof(model.WaiverReturnByEmail),
                "Choose at least one waiver return option (email or bring to event).");
        }

        if (model.WaiverRequired && model.WaiverReturnByEmail)
        {
            var effectiveContactEmail = NormalizeOptional(model.ContactEmail) ?? team.Email;
            if (string.IsNullOrWhiteSpace(effectiveContactEmail))
            {
                ModelState.AddModelError(
                    nameof(model.ContactEmail),
                    "Contact email is required when waiver return by email is enabled.");
            }
        }

        var hasExistingWaiverPdf = model.HasUploadedWaiverPdf && !model.RemoveUploadedWaiverPdf;
        var hasNewWaiverPdfUpload = model.WaiverPdfUpload is { Length: > 0 };
        if (model.WaiverRequired
            && string.Equals(model.WaiverMethod, TryOutSpotOpportunityWaiverMethods.Downloadable, StringComparison.Ordinal)
            && !hasExistingWaiverPdf
            && !hasNewWaiverPdfUpload)
        {
            ModelState.AddModelError(
                nameof(model.WaiverPdfUpload),
                "Upload a waiver PDF when downloadable waiver access is enabled.");
        }

        var sportExists = await dbContext.Sports
            .AsNoTracking()
            .AnyAsync(sport => sport.Id == model.SportId && sport.IsActive, cancellationToken);
        if (!sportExists)
        {
            ModelState.AddModelError(nameof(model.SportId), "Selected sport is not available.");
        }

        var hasTeamSportRestrictions = team.TeamSports.Any(teamSport => teamSport.IsActive);
        if (sportExists && hasTeamSportRestrictions)
        {
            var teamHasSport = team.TeamSports.Any(teamSport =>
                teamSport.IsActive && teamSport.SportId == model.SportId);
            if (!teamHasSport)
            {
                ModelState.AddModelError(nameof(model.SportId), "Selected sport is not assigned to this team.");
            }
        }

        var normalizedRegistrationDeadline = NormalizeUtc(model.RegistrationDeadline);
        var normalizedEventDate = NormalizeUtc(model.EventDate);
        var normalizedEventEndDate = NormalizeUtc(model.EventEndDate);
        var normalizedListingStartDate = NormalizeUtc(model.ListingStartDate);
        var normalizedListingEndDate = NormalizeUtc(model.ListingEndDate);
        var normalizedExpiresAt = NormalizeUtc(model.ExpiresAt);

        if (normalizedEventDate.HasValue
            && normalizedEventEndDate.HasValue
            && normalizedEventEndDate.Value < normalizedEventDate.Value)
        {
            ModelState.AddModelError(
                nameof(model.EventEndDate),
                "Event end date cannot be earlier than event start date.");
        }

        if (normalizedRegistrationDeadline.HasValue
            && normalizedEventDate.HasValue
            && normalizedRegistrationDeadline.Value > normalizedEventDate.Value)
        {
            ModelState.AddModelError(
                nameof(model.RegistrationDeadline),
                "Registration deadline must be on or before the event date.");
        }

        if (normalizedExpiresAt.HasValue && normalizedExpiresAt.Value <= DateTime.UtcNow)
        {
            ModelState.AddModelError(
                nameof(model.ExpiresAt),
                "Expiration must be in the future.");
        }

        if (normalizedListingStartDate.HasValue
            && normalizedListingEndDate.HasValue
            && normalizedListingEndDate.Value < normalizedListingStartDate.Value)
        {
            ModelState.AddModelError(
                nameof(model.ListingEndDate),
                "Listing end date cannot be earlier than listing start date.");
        }
    }

    private static IReadOnlyCollection<string> NormalizeOpportunityRegistrationEditorInput(
        TeamOpportunityEditorPageModel model,
        bool canConfigureTryoutRegistration)
    {
        var isTryoutType = string.Equals(
            NormalizeOptional(model.Type),
            "tryout",
            StringComparison.OrdinalIgnoreCase);
        if (!canConfigureTryoutRegistration || !isTryoutType)
        {
            model.RegistrationRequired = false;
        }

        if (!model.RegistrationRequired)
        {
            model.LimitRegistrationCapacity = false;
            model.MaxParticipants = null;
            model.RegistrationDeadline = null;
            model.WaiverRequired = false;
            model.WaiverMethod = null;
            model.WaiverReturnByEmail = false;
            model.WaiverReturnInPerson = false;
            model.RemoveUploadedWaiverPdf = false;
            model.RequiredRegistrationFieldCodes = [];
            model.RegistrationFee = 0;
            return [];
        }

        if (!model.LimitRegistrationCapacity)
        {
            model.MaxParticipants = null;
        }

        model.WaiverRequired = model.RegistrationRequired && model.WaiverRequired;
        model.WaiverMethod = model.WaiverRequired
            ? TryOutSpotOpportunityWaiverMethods.Normalize(model.WaiverMethod) ?? TryOutSpotOpportunityWaiverMethods.AtEvent
            : null;
        var isDownloadableWaiverMethod = model.WaiverRequired
            && string.Equals(
                model.WaiverMethod,
                TryOutSpotOpportunityWaiverMethods.Downloadable,
                StringComparison.Ordinal);
        model.WaiverReturnByEmail = isDownloadableWaiverMethod && model.WaiverReturnByEmail;
        model.WaiverReturnInPerson = isDownloadableWaiverMethod && model.WaiverReturnInPerson;
        if (!model.WaiverRequired)
        {
            model.WaiverPdfUpload = null;
        }

        var normalizedRequiredFieldCodes = TryOutSpotOpportunityRegistrationFields.NormalizeSelectedCodes(
            model.RequiredRegistrationFieldCodes)
            .Where(code => !string.Equals(code, TryOutSpotOpportunityRegistrationFields.WaiverSignature, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (model.WaiverRequired)
        {
            normalizedRequiredFieldCodes.Add(TryOutSpotOpportunityRegistrationFields.WaiverSignature);
        }

        model.RequiredRegistrationFieldCodes = normalizedRequiredFieldCodes.ToList();
        return model.RequiredRegistrationFieldCodes;
    }

    private void LogTeamOpportunityEditModelStateFailure(
        Guid userId,
        Guid teamId,
        Guid opportunityId,
        TeamOpportunityEditorPageModel model,
        string stage)
    {
        var modelStateErrors = BuildModelStateErrorMap();
        logger.LogWarning(
            "Team opportunity edit failed validation at stage {Stage}. UserId {UserId}, TeamId {TeamId}, OpportunityId {OpportunityId}, IsPublished {IsPublished}, RegistrationRequired {RegistrationRequired}, LimitRegistrationCapacity {LimitRegistrationCapacity}, MaxParticipants {MaxParticipants}, WaiverRequired {WaiverRequired}, WaiverMethod {WaiverMethod}, WaiverReturnByEmail {WaiverReturnByEmail}, WaiverReturnInPerson {WaiverReturnInPerson}, ListingStartDate {ListingStartDate}, ListingEndDate {ListingEndDate}, EventDate {EventDate}, EventEndDate {EventEndDate}, RegistrationDeadline {RegistrationDeadline}, RequiredRegistrationFieldCodes {RequiredRegistrationFieldCodes}, ModelStateErrors {@ModelStateErrors}",
            stage,
            userId,
            teamId,
            opportunityId,
            model.IsPublished,
            model.RegistrationRequired,
            model.LimitRegistrationCapacity,
            model.MaxParticipants,
            model.WaiverRequired,
            model.WaiverMethod,
            model.WaiverReturnByEmail,
            model.WaiverReturnInPerson,
            model.ListingStartDate,
            model.ListingEndDate,
            model.EventDate,
            model.EventEndDate,
            model.RegistrationDeadline,
            model.RequiredRegistrationFieldCodes,
            modelStateErrors);
    }

    private Dictionary<string, string[]> BuildModelStateErrorMap()
    {
        var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ModelState)
        {
            if (entry.Value is null || entry.Value.Errors.Count == 0)
            {
                continue;
            }

            var key = string.IsNullOrWhiteSpace(entry.Key)
                ? "<model>"
                : entry.Key;
            var messages = entry.Value.Errors
                .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage)
                    ? "Validation failed."
                    : error.ErrorMessage)
                .ToArray();
            errors[key] = messages;
        }

        return errors;
    }

    private string BuildTeamOpportunityEditFailureMessage()
    {
        var messages = ModelState.Values
            .SelectMany(entry => entry.Errors)
            .Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? null : error.ErrorMessage.Trim())
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.Ordinal)
            .Take(3)
            .ToArray();

        if (messages.Length == 0)
        {
            return "Update was not saved. Please review the form and try again.";
        }

        return $"Update was not saved. {string.Join(" ", messages)}";
    }

    private async Task EnsureCanPublishTeamOpportunityAsync(
        Guid teamId,
        TeamPostingAccess postingAccess,
        bool currentlyPublished,
        CancellationToken cancellationToken)
    {
        if (currentlyPublished || postingAccess.HasUnlimitedPosting)
        {
            return;
        }

        if (!postingAccess.HasLimitedPosting)
        {
            ModelState.AddModelError(
                nameof(TeamOpportunityEditorPageModel.IsPublished),
                "Your current membership does not include opportunity posting.");
            return;
        }

        var publishedInWindowCount = await GetPublishedOpportunityCountForWindowAsync(
            teamId,
            postingAccess.PublishingWindowMonths,
            cancellationToken);
        if (publishedInWindowCount >= postingAccess.PublishingLimit)
        {
            ModelState.AddModelError(
                nameof(TeamOpportunityEditorPageModel.IsPublished),
                $"{postingAccess.PlanLabel} includes up to {postingAccess.PublishingLimit} published opportunities every {postingAccess.PublishingWindowMonths} months.");
        }
    }

    private async Task<TeamPostingAccess> ResolveTeamPostingAccessAsync(Guid userId, CancellationToken cancellationToken)
    {
        var entitlements = await entitlementService.GetEntitlementsAsync(userId, cancellationToken);
        var activePlanCodes = entitlements?.ActivePlanCodes ?? [];

        if (activePlanCodes.Contains(TryOutSpotPlanCodes.EnterpriseOrganization, StringComparer.Ordinal))
        {
            return TeamPostingAccess.Enterprise;
        }

        if (activePlanCodes.Contains(TryOutSpotPlanCodes.TeamProfessional, StringComparer.Ordinal))
        {
            return TeamPostingAccess.Professional;
        }

        if (activePlanCodes.Contains(TryOutSpotPlanCodes.TeamBasic, StringComparer.Ordinal))
        {
            return TeamPostingAccess.Basic;
        }

        var featureCodes = entitlements?.FeatureCodes ?? [];
        if (!featureCodes.Contains(TryOutSpotFeatureCodes.PostLimitedOpportunities, StringComparer.Ordinal))
        {
            return TeamPostingAccess.None;
        }

        return TeamPostingAccess.FreeCoach;
    }

    private async Task<bool> CanConfigureTryoutRegistrationAsync(Guid userId, CancellationToken cancellationToken)
    {
        var entitlements = await entitlementService.GetEntitlementsAsync(userId, cancellationToken);
        var featureCodes = entitlements?.FeatureCodes ?? [];
        return featureCodes.Contains(TryOutSpotFeatureCodes.StandardRegistrationManagement, StringComparer.Ordinal)
            || featureCodes.Contains(TryOutSpotFeatureCodes.PremiumRegistrationManagement, StringComparer.Ordinal);
    }

    private async Task<bool> HasAdvancedOpportunitySearchAsync(Guid userId, CancellationToken cancellationToken)
    {
        var entitlements = await entitlementService.GetEntitlementsAsync(userId, cancellationToken);
        var featureCodes = entitlements?.FeatureCodes ?? [];
        return featureCodes.Contains(TryOutSpotFeatureCodes.AdvancedOpportunitySearch, StringComparer.Ordinal);
    }

    private async Task<TeamCreationAccess> ResolveTeamCreationAccessAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var managedTeamCount = await dbContext.UserTeamRoles
            .AsNoTracking()
            .Where(teamRole => teamRole.UserId == userId)
            .Where(teamRole => teamRole.IsActive)
            .Where(teamRole => teamRole.Team.IsActive)
            .Select(teamRole => teamRole.TeamId)
            .Distinct()
            .CountAsync(cancellationToken);
        if (managedTeamCount == 0)
        {
            return TeamCreationAccess.Allow;
        }

        var entitlements = await entitlementService.GetEntitlementsAsync(userId, cancellationToken);
        var featureCodes = entitlements?.FeatureCodes ?? [];
        if (featureCodes.Contains(TryOutSpotFeatureCodes.MultiTeamManagement, StringComparer.Ordinal))
        {
            return TeamCreationAccess.Allow;
        }

        return TeamCreationAccess.Blocked(
            "Your current membership allows one active team. Upgrade to Enterprise Organization to add additional teams.");
    }

    private string? ResolveZipCodeOrAddModelError(
        string? requestedZipCode,
        string? fallbackZipCode,
        string modelStateKey)
    {
        var normalizedRequestedZipCode = zipRadiusSearchService.NormalizeZipCode(requestedZipCode);
        if (!string.IsNullOrWhiteSpace(normalizedRequestedZipCode))
        {
            return normalizedRequestedZipCode;
        }

        var normalizedFallbackZipCode = zipRadiusSearchService.NormalizeZipCode(fallbackZipCode);
        if (!string.IsNullOrWhiteSpace(normalizedFallbackZipCode))
        {
            return normalizedFallbackZipCode;
        }

        ModelState.AddModelError(modelStateKey, "ZIP code is required and must be a valid 5-digit ZIP.");
        return null;
    }

    private async Task<IReadOnlyCollection<ManagedTeamRoleContext>> GetManagedTeamRoleContextsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var managedTeams = await dbContext.UserTeamRoles
            .AsNoTracking()
            .AsSplitQuery()
            .Where(teamRole => teamRole.UserId == userId)
            .Where(teamRole => teamRole.IsActive)
            .Where(teamRole => teamRole.Team.IsActive)
            .Include(teamRole => teamRole.Team)
                .ThenInclude(team => team.Organization)
            .Include(teamRole => teamRole.Team)
                .ThenInclude(team => team.TeamSports)
                    .ThenInclude(teamSport => teamSport.Sport)
            .Include(teamRole => teamRole.Team)
                .ThenInclude(team => team.Opportunities)
            .ToArrayAsync(cancellationToken);

        return managedTeams
            .Select(teamRole => new ManagedTeamRoleContext(teamRole.Team, teamRole.Role))
            .ToArray();
    }

    private async Task<ManagedTeamRoleContext?> GetManagedTeamRoleContextAsync(
        Guid userId,
        Guid teamId,
        CancellationToken cancellationToken)
    {
        var managedTeam = await dbContext.UserTeamRoles
            .AsSplitQuery()
            .Where(teamRole => teamRole.UserId == userId)
            .Where(teamRole => teamRole.TeamId == teamId)
            .Where(teamRole => teamRole.IsActive)
            .Where(teamRole => teamRole.Team.IsActive)
            .Include(teamRole => teamRole.Team)
                .ThenInclude(team => team.Organization)
            .Include(teamRole => teamRole.Team)
                .ThenInclude(team => team.TeamSports)
                    .ThenInclude(teamSport => teamSport.Sport)
            .Include(teamRole => teamRole.Team)
                .ThenInclude(team => team.Opportunities)
            .SingleOrDefaultAsync(cancellationToken);

        return managedTeam is null
            ? null
            : new ManagedTeamRoleContext(managedTeam.Team, managedTeam.Role);
    }

    private async Task<Dictionary<Guid, int>> GetPublishedOpportunityCountsForWindowAsync(
        IReadOnlyCollection<Guid> teamIds,
        int windowMonths,
        CancellationToken cancellationToken)
    {
        if (teamIds.Count == 0)
        {
            return [];
        }

        var windowStart = DateTime.UtcNow.AddMonths(-windowMonths);
        return await dbContext.Opportunities
            .AsNoTracking()
            .Where(opportunity => teamIds.Contains(opportunity.TeamId))
            .Where(opportunity => opportunity.PublishedAt != null
                && opportunity.PublishedAt >= windowStart)
            .Where(opportunity =>
                opportunity.IsActive
                || opportunity.UpdatedAt > opportunity.PublishedAt!.Value.AddHours(24))
            .GroupBy(opportunity => opportunity.TeamId)
            .Select(group => new
            {
                TeamId = group.Key,
                Count = group.Count()
            })
            .ToDictionaryAsync(group => group.TeamId, group => group.Count, cancellationToken);
    }

    private async Task<int> GetPublishedOpportunityCountForWindowAsync(
        Guid teamId,
        int windowMonths,
        CancellationToken cancellationToken)
    {
        var counts = await GetPublishedOpportunityCountsForWindowAsync([teamId], windowMonths, cancellationToken);
        return counts.TryGetValue(teamId, out var count) ? count : 0;
    }

    private static ManagedTeamOpportunitySummaryPageModel ToManagedTeamOpportunitySummary(
        Team team,
        string role,
        int publishedInWindowCount)
    {
        var activeOpportunities = team.Opportunities
            .Where(opportunity => opportunity.IsActive)
            .ToArray();
        var sports = team.TeamSports
            .Where(teamSport => teamSport.IsActive)
            .Select(teamSport => teamSport.Sport.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(sport => sport)
            .ToArray();

        return new ManagedTeamOpportunitySummaryPageModel
        {
            TeamId = team.Id,
            TeamName = team.Name,
            OrganizationName = team.Organization?.Name,
            LogoImageUrl = ResolveTeamLogoPublicUrl(team.Id, team.LogoImageUrl),
            Role = role,
            TeamLevel = team.TeamLevel,
            TeamDescription = NormalizeOptional(team.Description),
            GeographicScope = team.GeographicScope,
            City = team.City,
            State = team.State,
            ZipCode = team.ZipCode,
            IsSearchable = team.IsSearchable,
            IsContactInfoVisible = team.IsContactInfoVisible,
            ActiveOpportunityCount = activeOpportunities.Length,
            PublishedOpportunityCount = activeOpportunities.Count(opportunity => opportunity.IsPublished),
            PublishedInWindowCount = publishedInWindowCount,
            Sports = sports
        };
    }

    private static TeamOpportunitySummaryPageModel ToTeamOpportunitySummary(Opportunity opportunity)
    {
        return new TeamOpportunitySummaryPageModel
        {
            OpportunityId = opportunity.Id,
            TeamId = opportunity.TeamId,
            SportId = opportunity.SportId,
            SportName = opportunity.Sport.Name,
            Type = opportunity.Type,
            Title = opportunity.Title,
            Description = opportunity.Description,
            CompetitionLevel = opportunity.CompetitionLevel,
            AgeGroup = opportunity.AgeGroup,
            RegistrationFee = opportunity.RegistrationFee,
            RegistrationDeadline = opportunity.RegistrationDeadline,
            EventDate = opportunity.EventDate,
            EventEndDate = opportunity.EventEndDate,
            City = opportunity.City,
            State = opportunity.State,
            ZipCode = opportunity.ZipCode,
            WebsiteUrl = opportunity.WebsiteUrl,
            PdfUrl = opportunity.PdfUrl,
            IsPublished = opportunity.IsPublished,
            PublishedAt = opportunity.PublishedAt,
            ExpiresAt = opportunity.ExpiresAt,
            ViewCount = opportunity.ViewCount,
            UpdatedAt = opportunity.UpdatedAt
        };
    }

    private static RegistrationStatusCategory ClassifyRegistrationStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return RegistrationStatusCategory.Pending;
        }

        var normalizedStatus = status.Trim().ToLowerInvariant();
        if (normalizedStatus.Contains("accept", StringComparison.Ordinal)
            || normalizedStatus.Contains("approve", StringComparison.Ordinal)
            || normalizedStatus.Contains("confirm", StringComparison.Ordinal))
        {
            return RegistrationStatusCategory.Approved;
        }

        if (normalizedStatus.Contains("declin", StringComparison.Ordinal)
            || normalizedStatus.Contains("reject", StringComparison.Ordinal)
            || normalizedStatus.Contains("denied", StringComparison.Ordinal)
            || normalizedStatus.Contains("withdraw", StringComparison.Ordinal)
            || normalizedStatus.Contains("cancel", StringComparison.Ordinal))
        {
            return RegistrationStatusCategory.Declined;
        }

        return RegistrationStatusCategory.Pending;
    }

    private static string FormatRegistrationStatusLabel(string? status)
    {
        return ClassifyRegistrationStatus(status) switch
        {
            RegistrationStatusCategory.Approved => "Accepted",
            RegistrationStatusCategory.Declined => "Declined",
            _ => "Pending review"
        };
    }

    private static string ToRegistrationStatusCode(string? status)
    {
        return ClassifyRegistrationStatus(status) switch
        {
            RegistrationStatusCategory.Approved => RegistrationWorkflowStatusAccepted,
            RegistrationStatusCategory.Declined => RegistrationWorkflowStatusDeclined,
            _ => RegistrationWorkflowStatusPending
        };
    }

    private static string? NormalizeRegistrationWorkflowStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return null;
        }

        var normalized = status.Trim().ToLowerInvariant();
        if (normalized.Contains("accept", StringComparison.Ordinal)
            || normalized.Contains("approv", StringComparison.Ordinal)
            || normalized.Contains("confirm", StringComparison.Ordinal))
        {
            return RegistrationWorkflowStatusAccepted;
        }

        if (normalized.Contains("declin", StringComparison.Ordinal)
            || normalized.Contains("reject", StringComparison.Ordinal)
            || normalized.Contains("deni", StringComparison.Ordinal)
            || normalized.Contains("withdraw", StringComparison.Ordinal)
            || normalized.Contains("cancel", StringComparison.Ordinal))
        {
            return RegistrationWorkflowStatusDeclined;
        }

        if (normalized.Contains("pending", StringComparison.Ordinal)
            || normalized.Contains("review", StringComparison.Ordinal)
            || normalized.Contains("new", StringComparison.Ordinal)
            || normalized.Contains("submit", StringComparison.Ordinal))
        {
            return RegistrationWorkflowStatusPending;
        }

        return normalized switch
        {
            RegistrationWorkflowStatusAccepted => RegistrationWorkflowStatusAccepted,
            RegistrationWorkflowStatusDeclined => RegistrationWorkflowStatusDeclined,
            RegistrationWorkflowStatusPending => RegistrationWorkflowStatusPending,
            _ => null
        };
    }

    private static bool IsRegistrationMarkedPresent(string? attendanceStatus)
    {
        return string.Equals(
            attendanceStatus?.Trim(),
            RegistrationAttendancePresentStatus,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildPlayerDisplayName(string? firstName, string? lastName)
    {
        var displayName = $"{firstName} {lastName}".Trim();
        return string.IsNullOrWhiteSpace(displayName) ? "Player" : displayName;
    }

    private static decimal? CalculateConversionRate(int registrations, int views)
    {
        if (views <= 0)
        {
            return null;
        }

        return Math.Round((decimal)registrations * 100m / views, 1, MidpointRounding.AwayFromZero);
    }

    private static PlayerListingSummaryPageModel ToPlayerListingSummaryPageModel(PlayerListing listing)
    {
        return new PlayerListingSummaryPageModel
        {
            ListingId = listing.Id,
            ListingType = listing.ListingType,
            ListingTypeLabel = GetPlayerListingTypeLabel(listing.ListingType),
            Title = listing.Title,
            Description = listing.Description,
            PlayerId = listing.PlayerId,
            PlayerName = listing.Player == null ? null : $"{listing.Player.FirstName} {listing.Player.LastName}".Trim(),
            SportName = listing.Sport?.Name,
            AskingPrice = listing.AskingPrice,
            Currency = listing.Currency,
            Condition = listing.Condition,
            City = listing.City,
            State = listing.State,
            ZipCode = listing.ZipCode,
            IsPublished = listing.IsPublished,
            IsSearchable = listing.IsSearchable,
            PublishedAt = listing.PublishedAt,
            ExpiresAt = listing.ExpiresAt,
            UpdatedAt = listing.UpdatedAt
        };
    }

    private static string GetPlayerListingTypeLabel(string? listingType)
    {
        var option = GetPlayerListingTypeOption(listingType);
        if (option is not null)
        {
            return option.Label;
        }

        var fallback = string.IsNullOrWhiteSpace(listingType) ? "Listing" : listingType.Trim();
        fallback = fallback.Replace("_", " ", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(fallback)
            ? "Listing"
            : $"{char.ToUpperInvariant(fallback[0])}{fallback[1..]}";
    }

    private static string GetOpportunityTypeLabel(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return "Opportunity";
        }

        var normalized = type
            .Replace("_", " ", StringComparison.Ordinal)
            .Trim()
            .ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "Opportunity";
        }

        return normalized switch
        {
            "tryout" => "Tryout",
            "roster opening" => "Roster opening",
            "pickup player" => "Pickup player",
            "camp" => "Camp",
            "clinic" => "Clinic",
            "tournament" => "Tournament",
            "private workout" => "Private workout",
            _ => $"{char.ToUpperInvariant(normalized[0])}{normalized[1..]}"
        };
    }

    private static PlayerListingTypeSelectionPageItem? GetPlayerListingTypeOption(string? listingType)
    {
        if (string.IsNullOrWhiteSpace(listingType))
        {
            return null;
        }

        return PlayerListingTypeOptions.FirstOrDefault(option =>
            string.Equals(option.Code, listingType.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private string? NormalizePlayerListingType(string? listingType, string modelStateKey)
    {
        var normalized = TryOutSpotPlayerListingTypes.Normalize(listingType);
        if (normalized is null)
        {
            ModelState.AddModelError(
                modelStateKey,
                $"'{listingType}' is not a supported listing type. Supported values: {string.Join(", ", TryOutSpotPlayerListingTypes.Values)}.");
        }

        return normalized;
    }

    private async Task<IReadOnlyCollection<ManagedPlayerSelectionPageItem>> GetManagedPlayersAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var managedPlayers = await dbContext.UserPlayerRelationships
            .AsNoTracking()
            .Where(relationship => relationship.UserId == userId)
            .Where(relationship => relationship.CanManage)
            .Where(relationship => relationship.Player.IsActive)
            .Select(relationship => relationship.Player)
            .Distinct()
            .OrderBy(player => player.LastName)
            .ThenBy(player => player.FirstName)
            .ToArrayAsync(cancellationToken);

        return managedPlayers
            .Select(player => new ManagedPlayerSelectionPageItem(
                player.Id,
                $"{player.FirstName} {player.LastName}".Trim(),
                player.City,
                player.State,
                player.ZipCode))
            .ToArray();
    }

    private async Task ApplyPlayerListingPdfUploadChangesAsync(
        PlayerListing listing,
        Guid uploadedByUserId,
        IFormFile? uploadedPdf,
        bool removeUploadedPdf,
        string modelStateKey,
        CancellationToken cancellationToken)
    {
        if (uploadedPdf is null || uploadedPdf.Length == 0)
        {
            if (removeUploadedPdf)
            {
                await RemovePlayerListingPdfAsync(listing, cancellationToken);
            }

            return;
        }

        logger.LogInformation(
            "Player listing PDF upload requested. ListingId={ListingId} UserId={UserId} FileName={FileName} FileSizeBytes={FileSizeBytes}",
            listing.Id,
            uploadedByUserId,
            uploadedPdf.FileName,
            uploadedPdf.Length);

        var parsedUpload = await ParseUploadedPdfAsync(uploadedPdf, modelStateKey, cancellationToken);
        if (parsedUpload is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(listing.UploadedPdfObjectKey))
        {
            await pdfStorageService.DeletePdfAsync(listing.UploadedPdfObjectKey, cancellationToken);
        }

        try
        {
            var objectKey = BuildListingDocumentObjectKey(PlayerListingDocumentType, listing.Id, uploadedByUserId, parsedUpload.FileName);
            await pdfStorageService.UploadPdfAsync(objectKey, parsedUpload.Content, cancellationToken);
            listing.UploadedPdfObjectKey = objectKey;
            listing.UploadedPdfFileName = parsedUpload.FileName;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to upload player listing PDF to R2 for listing {ListingId}.",
                listing.Id);
            ModelState.AddModelError(modelStateKey, "We could not upload the PDF right now. Please try again.");
        }
    }

    private async Task ApplyOpportunityFlyerUploadChangesAsync(
        Opportunity opportunity,
        Guid uploadedByUserId,
        IFormFile? uploadedFlyer,
        bool removeUploadedFlyer,
        string modelStateKey,
        CancellationToken cancellationToken)
    {
        if (uploadedFlyer is null || uploadedFlyer.Length == 0)
        {
            if (removeUploadedFlyer)
            {
                await RemoveOpportunityPdfAsync(opportunity, cancellationToken);
            }

            return;
        }

        logger.LogInformation(
            "Opportunity flyer upload requested. OpportunityId={OpportunityId} UserId={UserId} FileName={FileName} FileSizeBytes={FileSizeBytes}",
            opportunity.Id,
            uploadedByUserId,
            uploadedFlyer.FileName,
            uploadedFlyer.Length);

        var parsedUpload = await ParseUploadedListingFlyerAsync(uploadedFlyer, modelStateKey, cancellationToken);
        if (parsedUpload is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(opportunity.UploadedPdfObjectKey))
        {
            await pdfStorageService.DeletePdfAsync(opportunity.UploadedPdfObjectKey, cancellationToken);
        }

        try
        {
            var objectKey = BuildListingDocumentObjectKey(OpportunityDocumentType, opportunity.Id, uploadedByUserId, parsedUpload.FileName);
            await pdfStorageService.UploadFileAsync(objectKey, parsedUpload.Content, parsedUpload.ContentType, cancellationToken);
            opportunity.UploadedPdfObjectKey = objectKey;
            opportunity.UploadedPdfFileName = parsedUpload.FileName;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to upload opportunity flyer to R2 for opportunity {OpportunityId}.",
                opportunity.Id);
            ModelState.AddModelError(modelStateKey, "We could not upload the flyer right now. Please try again.");
        }
    }

    private async Task ApplyOpportunityWaiverPdfUploadChangesAsync(
        Opportunity opportunity,
        Guid uploadedByUserId,
        IFormFile? uploadedPdf,
        bool removeUploadedPdf,
        string modelStateKey,
        CancellationToken cancellationToken)
    {
        if (uploadedPdf is null || uploadedPdf.Length == 0)
        {
            if (removeUploadedPdf)
            {
                await RemoveOpportunityWaiverPdfAsync(opportunity, cancellationToken);
            }

            return;
        }

        logger.LogInformation(
            "Opportunity waiver PDF upload requested. OpportunityId={OpportunityId} UserId={UserId} FileName={FileName} FileSizeBytes={FileSizeBytes}",
            opportunity.Id,
            uploadedByUserId,
            uploadedPdf.FileName,
            uploadedPdf.Length);

        var parsedUpload = await ParseUploadedPdfAsync(uploadedPdf, modelStateKey, cancellationToken);
        if (parsedUpload is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(opportunity.WaiverUploadedPdfObjectKey))
        {
            await pdfStorageService.DeletePdfAsync(opportunity.WaiverUploadedPdfObjectKey, cancellationToken);
        }

        try
        {
            var objectKey = BuildListingDocumentObjectKey(OpportunityWaiverDocumentType, opportunity.Id, uploadedByUserId, parsedUpload.FileName);
            await pdfStorageService.UploadPdfAsync(objectKey, parsedUpload.Content, cancellationToken);
            opportunity.WaiverUploadedPdfObjectKey = objectKey;
            opportunity.WaiverUploadedPdfFileName = parsedUpload.FileName;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Failed to upload opportunity waiver PDF to R2 for opportunity {OpportunityId}.",
                opportunity.Id);
            ModelState.AddModelError(modelStateKey, "We could not upload the waiver PDF right now. Please try again.");
        }
    }

    private async Task<UploadedPdfPayload?> ParseUploadedPdfAsync(
        IFormFile uploadedPdf,
        string modelStateKey,
        CancellationToken cancellationToken)
    {
        if (uploadedPdf.Length <= 0)
        {
            ModelState.AddModelError(modelStateKey, "Upload a PDF file.");
            return null;
        }

        if (uploadedPdf.Length > ListingFlyerMaxSizeBytes)
        {
            ModelState.AddModelError(
                modelStateKey,
                $"PDF files can be up to {ListingFlyerMaxSizeMegabytes} MB.");
            return null;
        }

        var extension = Path.GetExtension(uploadedPdf.FileName);
        if (!string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(modelStateKey, "Only PDF files are supported.");
            return null;
        }

        await using var stream = uploadedPdf.OpenReadStream();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);

        if (memoryStream.Length <= 0)
        {
            ModelState.AddModelError(modelStateKey, "Upload a PDF file.");
            return null;
        }

        if (memoryStream.Length > ListingFlyerMaxSizeBytes)
        {
            ModelState.AddModelError(
                modelStateKey,
                $"PDF files can be up to {ListingFlyerMaxSizeMegabytes} MB.");
            return null;
        }

        var content = memoryStream.ToArray();
        if (!LooksLikePdf(content))
        {
            ModelState.AddModelError(modelStateKey, "Uploaded file is not a valid PDF.");
            return null;
        }

        var fileName = Path.GetFileName(uploadedPdf.FileName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            fileName = "listing-flyer.pdf";
        }

        return new UploadedPdfPayload(fileName.Trim(), content);
    }

    private async Task<UploadedListingFlyerPayload?> ParseUploadedListingFlyerAsync(
        IFormFile uploadedFlyer,
        string modelStateKey,
        CancellationToken cancellationToken)
    {
        if (uploadedFlyer.Length <= 0)
        {
            ModelState.AddModelError(modelStateKey, "Upload a PDF or image flyer.");
            return null;
        }

        if (uploadedFlyer.Length > ListingFlyerMaxSizeBytes)
        {
            ModelState.AddModelError(
                modelStateKey,
                $"Flyer files can be up to {ListingFlyerMaxSizeMegabytes} MB.");
            return null;
        }

        var contentType = ResolveListingFlyerContentType(uploadedFlyer.ContentType, uploadedFlyer.FileName);
        if (contentType is null)
        {
            ModelState.AddModelError(modelStateKey, "Only PDF, JPG, PNG, or WEBP flyer files are supported.");
            return null;
        }

        await using var stream = uploadedFlyer.OpenReadStream();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);

        if (memoryStream.Length <= 0)
        {
            ModelState.AddModelError(modelStateKey, "Upload a PDF or image flyer.");
            return null;
        }

        if (memoryStream.Length > ListingFlyerMaxSizeBytes)
        {
            ModelState.AddModelError(
                modelStateKey,
                $"Flyer files can be up to {ListingFlyerMaxSizeMegabytes} MB.");
            return null;
        }

        var content = memoryStream.ToArray();
        if (string.Equals(contentType, "application/pdf", StringComparison.Ordinal)
            && !LooksLikePdf(content))
        {
            ModelState.AddModelError(modelStateKey, "Uploaded file is not a valid PDF.");
            return null;
        }

        if (contentType.StartsWith("image/", StringComparison.Ordinal)
            && !LooksLikeSupportedListingFlyerImage(content, contentType))
        {
            ModelState.AddModelError(modelStateKey, "Uploaded file is not a valid JPG, PNG, or WEBP image.");
            return null;
        }

        return new UploadedListingFlyerPayload(
            BuildSafeListingFlyerFileName(uploadedFlyer.FileName, contentType),
            content,
            contentType);
    }

    private async Task<UploadedImagePayload?> ParseUploadedImageAsync(
        IFormFile? uploadedImage,
        string modelStateKey,
        CancellationToken cancellationToken)
    {
        if (uploadedImage is null || uploadedImage.Length == 0)
        {
            return null;
        }

        if (uploadedImage.Length > ProfileImageMaxSizeBytes)
        {
            ModelState.AddModelError(
                modelStateKey,
                $"Image files can be up to {ProfileImageMaxSizeMegabytes} MB.");
            return null;
        }

        var contentType = NormalizeOptional(uploadedImage.ContentType)?.ToLowerInvariant();
        if (contentType is null || !SupportedImageContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(modelStateKey, "Only JPG, PNG, WEBP, or SVG images are supported.");
            return null;
        }

        await using var stream = uploadedImage.OpenReadStream();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);

        if (memoryStream.Length <= 0)
        {
            ModelState.AddModelError(modelStateKey, "Upload an image file.");
            return null;
        }

        if (memoryStream.Length > ProfileImageMaxSizeBytes)
        {
            ModelState.AddModelError(
                modelStateKey,
                $"Image files can be up to {ProfileImageMaxSizeMegabytes} MB.");
            return null;
        }

        return new UploadedImagePayload(
            BuildSafeUploadedFileName(uploadedImage.FileName, contentType),
            memoryStream.ToArray(),
            contentType);
    }

    private async Task ReplaceTeamLogoAsync(
        Team team,
        Organization? organization,
        Guid teamId,
        Guid uploadedByUserId,
        UploadedImagePayload? uploadedImage,
        bool removeImage,
        CancellationToken cancellationToken)
    {
        var previousObjectKey = TryGetStoredObjectKey(team.LogoImageUrl)
            ?? (organization is null ? null : TryGetStoredObjectKey(organization.LogoImageUrl));

        if (removeImage)
        {
            team.LogoImageUrl = null;
            if (organization is not null)
            {
                organization.LogoImageUrl = null;
            }
        }

        if (uploadedImage is not null)
        {
            var objectKey = BuildImageObjectKey(TeamLogoDocumentType, teamId, uploadedByUserId, uploadedImage.FileName);
            await imageStorageService.UploadImageAsync(objectKey, uploadedImage.Content, uploadedImage.ContentType, cancellationToken);
            var storedReference = ToStoredObjectReference(objectKey);
            team.LogoImageUrl = storedReference;
            if (organization is not null)
            {
                organization.LogoImageUrl = storedReference;
            }
        }

        if (previousObjectKey is not null && (removeImage || uploadedImage is not null))
        {
            await imageStorageService.DeleteImageAsync(previousObjectKey, cancellationToken);
        }
    }

    private async Task ReplacePlayerProfileImageAsync(
        Player player,
        Guid playerId,
        Guid uploadedByUserId,
        UploadedImagePayload? uploadedImage,
        bool removeImage,
        CancellationToken cancellationToken)
    {
        var previousObjectKey = TryGetStoredObjectKey(player.ProfileImageUrl);
        if (removeImage)
        {
            player.ProfileImageUrl = null;
        }

        if (uploadedImage is not null)
        {
            var objectKey = BuildImageObjectKey(PlayerProfileImageDocumentType, playerId, uploadedByUserId, uploadedImage.FileName);
            await imageStorageService.UploadImageAsync(objectKey, uploadedImage.Content, uploadedImage.ContentType, cancellationToken);
            player.ProfileImageUrl = ToStoredObjectReference(objectKey);
        }

        if (previousObjectKey is not null && (removeImage || uploadedImage is not null))
        {
            await imageStorageService.DeleteImageAsync(previousObjectKey, cancellationToken);
        }
    }

    private static bool LooksLikePdf(byte[] content)
    {
        return content.Length >= 5
            && content[0] == 0x25 // %
            && content[1] == 0x50 // P
            && content[2] == 0x44 // D
            && content[3] == 0x46 // F
            && content[4] == 0x2D; // -
    }

    private static bool LooksLikeSupportedListingFlyerImage(byte[] content, string contentType)
    {
        return contentType switch
        {
            "image/jpeg" => content.Length >= 3
                && content[0] == 0xFF
                && content[1] == 0xD8
                && content[2] == 0xFF,
            "image/png" => content.Length >= 8
                && content[0] == 0x89
                && content[1] == 0x50 // P
                && content[2] == 0x4E // N
                && content[3] == 0x47 // G
                && content[4] == 0x0D
                && content[5] == 0x0A
                && content[6] == 0x1A
                && content[7] == 0x0A,
            "image/webp" => content.Length >= 12
                && content[0] == 0x52 // R
                && content[1] == 0x49 // I
                && content[2] == 0x46 // F
                && content[3] == 0x46 // F
                && content[8] == 0x57 // W
                && content[9] == 0x45 // E
                && content[10] == 0x42 // B
                && content[11] == 0x50, // P
            _ => false
        };
    }

    private async Task RemovePlayerListingPdfAsync(PlayerListing listing, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(listing.UploadedPdfObjectKey))
        {
            await pdfStorageService.DeletePdfAsync(listing.UploadedPdfObjectKey, cancellationToken);
        }

        listing.UploadedPdfObjectKey = null;
        listing.UploadedPdfFileName = null;
    }

    private async Task RemoveOpportunityPdfAsync(Opportunity opportunity, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(opportunity.UploadedPdfObjectKey))
        {
            await pdfStorageService.DeletePdfAsync(opportunity.UploadedPdfObjectKey, cancellationToken);
        }

        opportunity.UploadedPdfObjectKey = null;
        opportunity.UploadedPdfFileName = null;
    }

    private async Task RemoveOpportunityWaiverPdfAsync(Opportunity opportunity, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(opportunity.WaiverUploadedPdfObjectKey))
        {
            await pdfStorageService.DeletePdfAsync(opportunity.WaiverUploadedPdfObjectKey, cancellationToken);
        }

        opportunity.WaiverUploadedPdfObjectKey = null;
        opportunity.WaiverUploadedPdfFileName = null;
    }

    private static string BuildListingDocumentPath(string listingType, Guid listingId)
    {
        return $"/listing-documents/{listingType}/{listingId}";
    }

    private static string BuildImageDocumentPath(string imageType, Guid itemId)
    {
        return $"/media/{imageType}/{itemId}";
    }

    private static string? NormalizeListingType(string listingType)
    {
        if (string.Equals(listingType, PlayerListingDocumentType, StringComparison.OrdinalIgnoreCase))
        {
            return PlayerListingDocumentType;
        }

        if (string.Equals(listingType, OpportunityDocumentType, StringComparison.OrdinalIgnoreCase))
        {
            return OpportunityDocumentType;
        }

        if (string.Equals(listingType, OpportunityWaiverDocumentType, StringComparison.OrdinalIgnoreCase))
        {
            return OpportunityWaiverDocumentType;
        }

        return null;
    }

    private static string BuildListingDocumentObjectKey(string listingType, Guid listingId, Guid userId, string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        return $"{listingType}/{listingId}/{DateTime.UtcNow:yyyyMMddHHmmss}-{userId}-{safeFileName}";
    }

    private static string BuildImageObjectKey(string imageType, Guid itemId, Guid userId, string fileName)
    {
        var safeFileName = Path.GetFileName(fileName);
        return $"{imageType}/{itemId}/{DateTime.UtcNow:yyyyMMddHHmmss}-{userId}-{safeFileName}";
    }

    private static string ToStoredObjectReference(string objectKey)
    {
        return $"{R2ObjectStoragePrefix}{objectKey}";
    }

    private static string? TryGetStoredObjectKey(string? storedValue)
    {
        var normalized = NormalizeOptional(storedValue);
        if (normalized is null || !normalized.StartsWith(R2ObjectStoragePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var objectKey = normalized[R2ObjectStoragePrefix.Length..];
        return string.IsNullOrWhiteSpace(objectKey) ? null : objectKey;
    }

    private static string? ResolveTeamLogoPublicUrl(Guid? teamId, string? teamLogoImageValue)
    {
        if (teamId.HasValue && TryGetStoredObjectKey(teamLogoImageValue) is not null)
        {
            return BuildImageDocumentPath("team-logos", teamId.Value);
        }

        return NormalizeOptional(teamLogoImageValue);
    }

    private static string? ResolvePlayerProfileImagePublicUrl(Guid? playerId, string? playerProfileImageValue)
    {
        if (playerId.HasValue && TryGetStoredObjectKey(playerProfileImageValue) is not null)
        {
            return BuildImageDocumentPath("player-profiles", playerId.Value);
        }

        return NormalizeOptional(playerProfileImageValue);
    }

    private static string BuildSafeUploadedFileName(string originalFileName, string contentType)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "image";
        }

        var extension = contentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/svg+xml" => ".svg",
            _ => Path.GetExtension(originalFileName)
        };

        return $"{baseName.Trim()}{extension}";
    }

    private static string BuildSafeListingFlyerFileName(string originalFileName, string contentType)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "listing-flyer";
        }

        var invalidFileNameChars = Path.GetInvalidFileNameChars();
        var safeBaseName = new string(
            baseName
                .Trim()
                .Select(character => invalidFileNameChars.Contains(character) ? '-' : character)
                .ToArray());
        if (string.IsNullOrWhiteSpace(safeBaseName))
        {
            safeBaseName = "listing-flyer";
        }

        var extension = GetListingFlyerExtension(contentType);
        var maxBaseNameLength = Math.Max(1, 260 - extension.Length);
        if (safeBaseName.Length > maxBaseNameLength)
        {
            safeBaseName = safeBaseName[..maxBaseNameLength];
        }

        return $"{safeBaseName}{extension}";
    }

    private static string? ResolveListingFlyerContentType(string? contentType, string? fileNameOrUrl)
    {
        var normalizedContentType = NormalizeOptional(contentType)?.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (string.Equals(normalizedContentType, "image/jpg", StringComparison.Ordinal))
        {
            normalizedContentType = "image/jpeg";
        }

        if (normalizedContentType is not null
            && SupportedListingFlyerContentTypes.Contains(normalizedContentType, StringComparer.Ordinal))
        {
            return normalizedContentType;
        }

        var path = NormalizeOptional(fileNameOrUrl);
        if (path is null)
        {
            return null;
        }

        if (Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri))
        {
            path = absoluteUri.AbsolutePath;
        }

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => null
        };
    }

    private static string ResolveListingDocumentContentType(
        string? storedContentType,
        string? fileName,
        string? objectKey)
    {
        var contentType = ResolveListingFlyerContentType(storedContentType, fileName)
            ?? ResolveListingFlyerContentType(null, objectKey);
        return contentType ?? "application/octet-stream";
    }

    private static bool IsListingFlyerImageReference(string? fileNameOrUrl)
    {
        var contentType = ResolveListingFlyerContentType(null, fileNameOrUrl);
        return contentType is not null && contentType.StartsWith("image/", StringComparison.Ordinal);
    }

    private static string GetListingFlyerExtension(string contentType)
    {
        return contentType switch
        {
            "application/pdf" => ".pdf",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => Path.GetExtension(contentType)
        };
    }

    private async Task<(string? ObjectKey, string? FileName)?> GetPdfReferenceAsync(
        string listingType,
        Guid listingId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(listingType, PlayerListingDocumentType, StringComparison.Ordinal))
        {
            var listing = await dbContext.PlayerListings
                .AsNoTracking()
                .Where(item => item.Id == listingId)
                .Select(item => new { item.UploadedPdfObjectKey, item.UploadedPdfFileName })
                .SingleOrDefaultAsync(cancellationToken);
            return listing is null ? null : (listing.UploadedPdfObjectKey, listing.UploadedPdfFileName);
        }

        if (string.Equals(listingType, OpportunityDocumentType, StringComparison.Ordinal))
        {
            var listing = await dbContext.Opportunities
                .AsNoTracking()
                .Where(item => item.Id == listingId)
                .Select(item => new { item.UploadedPdfObjectKey, item.UploadedPdfFileName })
                .SingleOrDefaultAsync(cancellationToken);
            return listing is null ? null : (listing.UploadedPdfObjectKey, listing.UploadedPdfFileName);
        }

        if (string.Equals(listingType, OpportunityWaiverDocumentType, StringComparison.Ordinal))
        {
            var listing = await dbContext.Opportunities
                .AsNoTracking()
                .Where(item => item.Id == listingId)
                .Select(item => new { item.WaiverUploadedPdfObjectKey, item.WaiverUploadedPdfFileName })
                .SingleOrDefaultAsync(cancellationToken);
            return listing is null ? null : (listing.WaiverUploadedPdfObjectKey, listing.WaiverUploadedPdfFileName);
        }

        return null;
    }

    private async Task<bool> CanAccessListingDocumentAsync(
        string listingType,
        Guid listingId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(listingType, PlayerListingDocumentType, StringComparison.Ordinal))
        {
            if (await CanAccessPlayerListingDocumentAsPublicAsync(listingId, cancellationToken))
            {
                return true;
            }

            var currentUserId = await GetCurrentAuthenticatedWebUserIdAsync();
            if (!currentUserId.HasValue)
            {
                return false;
            }

            return await dbContext.PlayerListings
                .AsNoTracking()
                .AnyAsync(listing =>
                    listing.Id == listingId
                    && listing.UserId == currentUserId.Value
                    && listing.IsActive,
                    cancellationToken);
        }

        if (string.Equals(listingType, OpportunityDocumentType, StringComparison.Ordinal))
        {
            if (await CanAccessTeamOpportunityDocumentAsPublicAsync(listingId, cancellationToken))
            {
                return true;
            }

            var currentUserId = await GetCurrentAuthenticatedWebUserIdAsync();
            if (!currentUserId.HasValue)
            {
                return false;
            }

            return await dbContext.Opportunities
                .AsNoTracking()
                .Where(opportunity => opportunity.Id == listingId)
                .Where(opportunity => opportunity.IsActive)
                .AnyAsync(opportunity =>
                    opportunity.Team.UserTeamRoles.Any(teamRole =>
                        teamRole.UserId == currentUserId.Value
                        && teamRole.IsActive
                        && teamRole.Team.IsActive),
                    cancellationToken);
        }

        if (string.Equals(listingType, OpportunityWaiverDocumentType, StringComparison.Ordinal))
        {
            if (await CanAccessTeamOpportunityWaiverDocumentAsPublicAsync(listingId, cancellationToken))
            {
                return true;
            }

            var currentUserId = await GetCurrentAuthenticatedWebUserIdAsync();
            if (!currentUserId.HasValue)
            {
                return false;
            }

            return await dbContext.Opportunities
                .AsNoTracking()
                .Where(opportunity => opportunity.Id == listingId)
                .Where(opportunity => opportunity.IsActive)
                .AnyAsync(opportunity =>
                    opportunity.Team.UserTeamRoles.Any(teamRole =>
                        teamRole.UserId == currentUserId.Value
                        && teamRole.IsActive
                        && teamRole.Team.IsActive),
                    cancellationToken);
        }

        return false;
    }

    private async Task<bool> CanAccessPlayerListingDocumentAsPublicAsync(
        Guid listingId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return await dbContext.PlayerListings
            .AsNoTracking()
            .AnyAsync(listing =>
                listing.Id == listingId
                && listing.IsActive
                && listing.IsPublished
                && listing.IsSearchable
                && (listing.ExpiresAt == null || listing.ExpiresAt > now),
                cancellationToken);
    }

    private async Task<bool> CanAccessTeamOpportunityDocumentAsPublicAsync(
        Guid opportunityId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return await dbContext.Opportunities
            .AsNoTracking()
            .AnyAsync(opportunity =>
                opportunity.Id == opportunityId
                && opportunity.IsActive
                && opportunity.IsPublished
                && (opportunity.ExpiresAt == null || opportunity.ExpiresAt > now)
                && opportunity.Team.IsActive
                && opportunity.Team.IsSearchable
                && opportunity.Team.IsContactInfoVisible
                && (opportunity.Team.Organization == null || opportunity.Team.Organization.IsContactInfoVisible),
                cancellationToken);
    }

    private async Task<bool> CanAccessTeamOpportunityWaiverDocumentAsPublicAsync(
        Guid opportunityId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return await dbContext.Opportunities
            .AsNoTracking()
            .AnyAsync(opportunity =>
                opportunity.Id == opportunityId
                && opportunity.IsActive
                && opportunity.IsPublished
                && (opportunity.ExpiresAt == null || opportunity.ExpiresAt > now)
                && opportunity.RegistrationRequired
                && (opportunity.WaiverRequired
                    || (opportunity.RegistrationRequiredFieldCodes != null
                        && opportunity.RegistrationRequiredFieldCodes.Contains(TryOutSpotOpportunityRegistrationFields.WaiverSignature)))
                && opportunity.Team.IsActive
                && opportunity.Team.IsSearchable,
                cancellationToken);
    }

    private async Task<Guid?> GetCurrentAuthenticatedWebUserIdAsync()
    {
        var authenticationResult = await HttpContext.AuthenticateAsync(TryOutSpotAuthenticationSchemes.WebCookie);
        var userIdClaim = authenticationResult.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return null;
        }

        return userId;
    }

    private sealed record UploadedPdfPayload(string FileName, byte[] Content);

    private sealed record UploadedListingFlyerPayload(string FileName, byte[] Content, string ContentType);

    private static IReadOnlyCollection<ExternalProfileLinkPageItem> BuildPlayerExternalLinkItems(
        string? serializedLinks,
        params (string Key, string Label)[] labelPairs)
    {
        return BuildPlayerExternalLinkItems(
            serializedLinks,
            includedKeys: null,
            labelPairs);
    }

    private static IReadOnlyCollection<ExternalProfileLinkPageItem> BuildPlayerExternalLinkItems(
        string? serializedLinks,
        IReadOnlyCollection<string>? includedKeys,
        params (string Key, string Label)[] labelPairs)
    {
        if (string.IsNullOrWhiteSpace(serializedLinks))
        {
            return [];
        }

        try
        {
            var parsedLinks = JsonSerializer.Deserialize<Dictionary<string, string>>(serializedLinks);
            if (parsedLinks is null || parsedLinks.Count == 0)
            {
                return [];
            }

            var labelsByKey = labelPairs.ToDictionary(
                pair => pair.Key,
                pair => pair.Label,
                StringComparer.OrdinalIgnoreCase);
            var orderedKeys = labelPairs
                .Select(pair => pair.Key)
                .ToArray();
            var includedKeysLookup = includedKeys is null
                ? null
                : new HashSet<string>(includedKeys, StringComparer.OrdinalIgnoreCase);

            var links = new List<ExternalProfileLinkPageItem>();
            foreach (var key in orderedKeys)
            {
                if (includedKeysLookup is not null && !includedKeysLookup.Contains(key))
                {
                    continue;
                }

                if (!parsedLinks.TryGetValue(key, out var linkValue))
                {
                    continue;
                }

                if (!TryNormalizeAbsoluteLink(linkValue, out var normalizedLink))
                {
                    continue;
                }

                var label = labelsByKey.TryGetValue(key, out var configuredLabel)
                    ? configuredLabel
                    : key;
                links.Add(new ExternalProfileLinkPageItem(label, normalizedLink));
            }

            return links;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static IReadOnlyCollection<string> ParsePlayerListingVisibleSocialKeys(string? serializedKeys)
    {
        if (string.IsNullOrWhiteSpace(serializedKeys))
        {
            return GetDefaultVisibleSocialLinkKeys();
        }

        try
        {
            var parsedKeys = JsonSerializer.Deserialize<string[]>(serializedKeys);
            return NormalizePlayerListingVisibleSocialKeys(parsedKeys);
        }
        catch (JsonException)
        {
            return GetDefaultVisibleSocialLinkKeys();
        }
    }

    private static IReadOnlyCollection<string> NormalizePlayerListingVisibleSocialKeys(
        IEnumerable<string>? selectedKeys)
    {
        if (selectedKeys is null)
        {
            return [];
        }

        var validKeys = GetAllPlayerListingSocialDisplayKeys();
        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in selectedKeys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var normalizedKey = key.Trim();
            if (validKeys.Contains(normalizedKey))
            {
                selected.Add(normalizedKey);
            }
        }

        return validKeys
            .Where(selected.Contains)
            .ToArray();
    }

    private static string? SerializePlayerListingVisibleSocialKeys(IEnumerable<string>? selectedKeys)
    {
        if (selectedKeys is null)
        {
            return null;
        }

        var normalized = NormalizePlayerListingVisibleSocialKeys(selectedKeys);
        return JsonSerializer.Serialize(normalized);
    }

    private static IReadOnlyCollection<string> GetDefaultVisibleSocialLinkKeys()
    {
        return GetAllPlayerListingSocialDisplayKeys();
    }

    private static IReadOnlyCollection<string> GetAllPlayerListingSocialDisplayKeys()
    {
        return [.. PlayerListingSocialDisplayOptions.Select(option => option.Key), .. PlayerListingVideoDisplayOptions.Select(option => option.Key)];
    }

    private static bool TryNormalizeAbsoluteLink(string? value, out string normalizedLink)
    {
        normalizedLink = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        normalizedLink = uri.AbsoluteUri;
        return true;
    }

    private async Task<IReadOnlyCollection<Subscription>> GetActivePaidAccountMembershipsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var subscriptions = await dbContext.Subscriptions
            .Where(subscription => subscription.UserId == userId
                && subscription.ScopeType == TryOutSpotSubscriptionScopeTypes.Account
                && subscription.StripeSubscriptionId != null
                && subscription.StripeSubscriptionId != string.Empty)
            .OrderByDescending(subscription => subscription.UpdatedAt)
            .ToArrayAsync(cancellationToken);

        return subscriptions
            .Where(IsActivePaidSubscription)
            .ToArray();
    }

    private async Task<DateTime?> SchedulePaidMembershipCancellationAtPeriodEndAsync(
        Guid userId,
        IReadOnlyCollection<Subscription> cancelablePaidMemberships,
        CancellationToken cancellationToken)
    {
        DateTime? earliestPeriodEnd = null;

        var membershipsByStripeSubscription = cancelablePaidMemberships
            .Where(subscription => !string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
            .GroupBy(subscription => subscription.StripeSubscriptionId!, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(subscription => subscription.UpdatedAt).First())
            .ToArray();

        foreach (var membership in membershipsByStripeSubscription)
        {
            var stripeSubscriptionId = membership.StripeSubscriptionId!;
            var snapshot = await stripeBillingService.ScheduleCancellationAtPeriodEndAsync(
                stripeSubscriptionId,
                BuildCancelMembershipIdempotencyKey(userId, stripeSubscriptionId),
                cancellationToken);

            if (snapshot is null)
            {
                throw new InvalidOperationException(
                    $"Stripe did not return subscription details for '{stripeSubscriptionId}'.");
            }

            await stripeSubscriptionSyncService.ApplyStripeSubscriptionAsync(snapshot, cancellationToken);

            if (snapshot.CurrentPeriodEnd is not null
                && (earliestPeriodEnd is null || snapshot.CurrentPeriodEnd < earliestPeriodEnd))
            {
                earliestPeriodEnd = snapshot.CurrentPeriodEnd;
            }
        }

        return earliestPeriodEnd;
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
        var subscriptions = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .Where(subscription => subscription.StripeCustomerId != null && subscription.StripeCustomerId != string.Empty)
            .ToListAsync(cancellationToken);

        return subscriptions
            .OrderByDescending(subscription =>
                TryOutSpotBillingCatalog.IsEntitlingSubscriptionStatus(subscription.Status))
            .ThenByDescending(subscription => subscription.UpdatedAt)
            .Select(subscription => subscription.StripeCustomerId)
            .FirstOrDefault();
    }

    private static bool HasEntitlingRecommendedPlanSubscription(
        IEnumerable<Subscription> subscriptions,
        IReadOnlyCollection<BillingPlanResponse> recommendedPlans)
    {
        var planCodes = recommendedPlans
            .Select(plan => plan.Code)
            .ToHashSet(StringComparer.Ordinal);

        return subscriptions.Any(subscription =>
        {
            var normalizedPlanCode = TryOutSpotBillingCatalog.NormalizePlanCode(subscription.PlanType);
            return normalizedPlanCode is not null
                && planCodes.Contains(normalizedPlanCode)
                && TryOutSpotBillingCatalog.IsEntitlingSubscriptionStatus(subscription.Status);
        });
    }

    private async Task<AccountSettingsPageModel> BuildAccountSettingsPageModelAsync(
        User user,
        CancellationToken cancellationToken)
    {
        var roles = await GetCanonicalPublicRolesAsync(user);
        var entitlements = await entitlementService.GetEntitlementsAsync(user.Id, cancellationToken);
        var subscriptions = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(currentSubscription => currentSubscription.UserId == user.Id)
            .OrderByDescending(currentSubscription => currentSubscription.UpdatedAt)
            .ToArrayAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var complimentaryGrants = await dbContext.ComplimentaryPlanGrants
            .AsNoTracking()
            .Where(grant => grant.UserId == user.Id)
            .OrderByDescending(grant => grant.RevokedAt == null
                && grant.StartsAt <= now
                && (grant.EndsAt == null || grant.EndsAt > now))
            .ThenByDescending(grant => grant.UpdatedAt)
            .ToArrayAsync(cancellationToken);
        var activePaidSubscriptions = subscriptions.Where(IsActivePaidSubscription).ToArray();
        var activeComplimentaryGrants = complimentaryGrants
            .Where(grant => ComplimentaryPlanGrantMapper.IsActive(grant, now))
            .ToArray();
        var cancelablePaidMemberships = activePaidSubscriptions
            .Where(subscription =>
                !subscription.CancelAtPeriodEnd
                && !string.IsNullOrWhiteSpace(subscription.StripeSubscriptionId))
            .ToArray();
        var scheduledPaidCancellationAt = activePaidSubscriptions
            .Where(subscription => subscription.CancelAtPeriodEnd && subscription.CurrentPeriodEnd is not null)
            .Select(subscription => subscription.CurrentPeriodEnd)
            .OrderBy(date => date)
            .FirstOrDefault();
        var hasStripeCustomer = subscriptions.Any(subscription =>
            !string.IsNullOrWhiteSpace(subscription.StripeCustomerId));
        var hasPendingPaidPlanSelection = activePaidSubscriptions.Length == 0 && subscriptions.Any(subscription =>
        {
            var plan = TryOutSpotBillingCatalog.GetPlan(subscription.PlanType);
            if (plan?.RequiresStripeSubscription != true)
            {
                return false;
            }

            return string.Equals(subscription.Status, "plan_selected", StringComparison.OrdinalIgnoreCase)
                || string.Equals(subscription.Status, "checkout_started", StringComparison.OrdinalIgnoreCase);
        });
        var hasLocalPassword = await userManager.HasPasswordAsync(user);
        var dashboardActivityPreferences = await dashboardActivityService.GetPreferencesAsync(user.Id, cancellationToken);

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
            CurrentPlanName = FormatCurrentPlanName(activePaidSubscriptions, activeComplimentaryGrants),
            CurrentPlanStatus = FormatCurrentPlanStatus(activePaidSubscriptions, activeComplimentaryGrants),
            RecommendedPlans = GetPlansForAccountTypes(roles),
            FeatureCodes = entitlements?.FeatureCodes ?? [],
            StripeCheckoutConfigured = stripeBillingOptions.IsConfigured,
            HasStripeCustomer = hasStripeCustomer,
            HasPendingPaidPlanSelection = hasPendingPaidPlanSelection,
            MembershipSummaries = BuildMembershipSummaries(subscriptions, complimentaryGrants, now),
            CanCancelPaidMembership = cancelablePaidMemberships.Length > 0 && stripeBillingOptions.IsConfigured,
            HasScheduledPaidCancellation = scheduledPaidCancellationAt is not null,
            ScheduledPaidCancellationAt = scheduledPaidCancellationAt,
            DashboardActivityPreferences = dashboardActivityPreferences
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
        if (!result.Succeeded || result.Principal is null)
        {
            logger.LogInformation("Web cookie authentication failed for request path {Path}.", HttpContext.Request.Path);
            return null;
        }

        var userIdClaim = result.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            logger.LogInformation(
                "Web cookie did not contain a valid user id for request path {Path}.",
                HttpContext.Request.Path);
            return null;
        }

        var user = await userManager.FindByIdAsync(userId.ToString());
        var tokenSecurityStamp = result.Principal?.FindFirstValue("security_stamp");
        if (user is not { IsActive: true })
        {
            logger.LogInformation(
                "Invalidating web cookie for user {UserId} because the user is missing or inactive.",
                userId);
            await HttpContext.SignOutAsync(TryOutSpotAuthenticationSchemes.WebCookie);
            return null;
        }

        if (!string.Equals(user.SecurityStamp, tokenSecurityStamp, StringComparison.Ordinal))
        {
            logger.LogInformation(
                "Invalidating web cookie for user {UserId} because the security stamp no longer matches.",
                userId);
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

    private IActionResult RedirectToLocalOrSettings(string? returnUrl)
    {
        return ResolveLocalReturnUrl(returnUrl) is { } localReturnUrl
            ? LocalRedirect(localReturnUrl)
            : RedirectToAction(nameof(Settings));
    }

    private string? ResolveLocalReturnUrl(string? returnUrl)
    {
        return !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : null;
    }

    private static IReadOnlyCollection<AccountTypeSelectionItem> GetAccountTypeOptions(
        IEnumerable<string> selectedAccountTypes)
    {
        var selected = selectedAccountTypes
            .Select(TryOutSpotRoles.NormalizePublicRegistrationRole)
            .Where(role => role is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return
        [
            new(
                TryOutSpotRoles.Parent,
                "Parent or guardian",
                "Adult account for managing one or more player profiles and registrations. The account holder is not shown in player search.",
                true,
                selected.Contains(TryOutSpotRoles.Parent)),
            new(
                TryOutSpotRoles.Player,
                "Player",
                "Use when the player is creating their own account for profile and discovery workflows.",
                true,
                selected.Contains(TryOutSpotRoles.Player)),
            new(
                TryOutSpotRoles.TeamRepresentative,
                "Team representative",
                "Team role. Eligible for Free Coach and paid team plans.",
                true,
                selected.Contains(TryOutSpotRoles.TeamRepresentative))
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

        if (accountTypes.Count > 0
            && !TryOutSpotRoles.TryValidateSingleRolePerBundle(accountTypes, out var validationError))
        {
            ModelState.AddModelError(modelStateKey, validationError ?? "Invalid role selection.");
        }

        return accountTypes;
    }

    private static IReadOnlyCollection<string> BuildRequestedAccountTypesFromBundles(
        IReadOnlyCollection<string>? accountTypes,
        string? playerParentRole,
        string? teamRole)
    {
        var selected = new List<string>();

        if (accountTypes is not null && accountTypes.Count > 0)
        {
            selected.AddRange(accountTypes);
        }

        if (!string.IsNullOrWhiteSpace(playerParentRole))
        {
            selected.Add(playerParentRole);
        }

        if (!string.IsNullOrWhiteSpace(teamRole))
        {
            selected.Add(teamRole);
        }

        return selected;
    }

    private async Task<string?> NormalizeCurrentUserPublicRoleBundlesAsync(
        User user,
        CancellationToken cancellationToken)
    {
        var currentPublicRoles = (await userManager.GetRolesAsync(user))
            .Where(role => TryOutSpotRoles.PublicOrLegacyRegistrationRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (currentPublicRoles.Length == 0)
        {
            return null;
        }

        var selectedPlayerRole = currentPublicRoles
            .FirstOrDefault(TryOutSpotRoles.IsPlayerParentRole);
        var selectedTeamRole = TryOutSpotRoles.SelectHighestPrecedenceTeamRole(currentPublicRoles);

        var normalizedRoles = new List<string>();
        if (!string.IsNullOrWhiteSpace(selectedPlayerRole))
        {
            normalizedRoles.Add(selectedPlayerRole);
        }

        if (!string.IsNullOrWhiteSpace(selectedTeamRole))
        {
            normalizedRoles.Add(selectedTeamRole);
        }

        if (normalizedRoles.Count == 0)
        {
            return null;
        }

        var normalizedSet = normalizedRoles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentSet = currentPublicRoles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (normalizedSet.SetEquals(currentSet))
        {
            return null;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var removeResult = await userManager.RemoveFromRolesAsync(user, currentPublicRoles);
        if (!removeResult.Succeeded)
        {
            logger.LogWarning(
                "Unable to normalize account-type bundles for user {UserId}: {Errors}",
                user.Id,
                string.Join(" ", removeResult.Errors.Select(error => error.Description)));
            return null;
        }

        var addResult = await userManager.AddToRolesAsync(user, normalizedRoles);
        if (!addResult.Succeeded)
        {
            logger.LogWarning(
                "Unable to add normalized account-type bundles for user {UserId}: {Errors}",
                user.Id,
                string.Join(" ", addResult.Errors.Select(error => error.Description)));
            return null;
        }

        user.UpdatedAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);
        await transaction.CommitAsync(cancellationToken);
        await SignInWebUserAsync(user, isPersistent: true);

        var keptTeamRole = selectedTeamRole is null ? "none" : selectedTeamRole;
        logger.LogInformation(
            "Normalized legacy account-type roles for user {UserId}. Kept player bundle role '{PlayerRole}' and team bundle role '{TeamRole}'.",
            user.Id,
            selectedPlayerRole ?? "none",
            keptTeamRole);

        return $"We standardized account types to one selection per bundle. Kept {selectedPlayerRole ?? "no parent/player role"} and {keptTeamRole}.";
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

    private async Task SendVerificationEmailIfEligibleAsync(string? normalizedEmail, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(normalizedEmail))
        {
            return;
        }

        var user = await userManager.FindByEmailAsync(normalizedEmail);
        if (user is not { IsActive: true, EmailConfirmed: false })
        {
            return;
        }

        var confirmationToken = await userManager.GenerateEmailConfirmationTokenAsync(user);
        await accountEmailSender.SendEmailConfirmationTokenAsync(user, confirmationToken, cancellationToken);
    }

    private static bool HasAnyRole(IReadOnlyCollection<string> roles, params string[] candidates)
    {
        return roles.Any(role => candidates.Contains(role, StringComparer.OrdinalIgnoreCase));
    }

    private async Task<IReadOnlyCollection<string>> GetCanonicalPublicRolesAsync(User user)
    {
        return (await userManager.GetRolesAsync(user))
            .Select(TryOutSpotRoles.NormalizePublicRegistrationRole)
            .Where(role => role is not null)
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(role => role)
            .ToArray();
    }

    private async Task<AccountTypeUpdateResult> UpdateAccountTypesInternalAsync(
        User user,
        IReadOnlyCollection<string> requestedAccountTypes,
        CancellationToken cancellationToken)
    {
        var validatedAccountTypes = GetValidatedPublicAccountTypes(requestedAccountTypes, nameof(requestedAccountTypes));
        if (validatedAccountTypes.Count == 0)
        {
            return AccountTypeUpdateResult.Failed("Choose at least one account type.");
        }

        var requestedRoles = validatedAccountTypes.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentRoles = (await userManager.GetRolesAsync(user))
            .Where(role => TryOutSpotRoles.PublicOrLegacyRegistrationRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        var hasCurrentTeamRole = currentRoles.Any(TryOutSpotRoles.IsTeamBundleRole);
        var hasRequestedTeamRole = requestedRoles.Any(TryOutSpotRoles.IsTeamBundleRole);
        var hasCurrentPlayerParentRole = HasAnyRole(currentRoles, TryOutSpotRoles.Parent, TryOutSpotRoles.Player);
        var hasRequestedPlayerParentRole = requestedRoles.Contains(TryOutSpotRoles.Parent)
            || requestedRoles.Contains(TryOutSpotRoles.Player);

        if (hasCurrentTeamRole && !hasRequestedTeamRole)
        {
            var hasActiveTeamPaidSubscription = await HasActivePaidBundleSubscriptionAsync(user.Id, BundleType.Team, cancellationToken);
            if (hasActiveTeamPaidSubscription)
            {
                return AccountTypeUpdateResult.Failed(
                    "Team roles stay active while a paid team subscription is active. Cancel or downgrade team billing first, then remove team roles after billing ends.");
            }
        }

        if (hasCurrentPlayerParentRole && !hasRequestedPlayerParentRole)
        {
            var hasActivePlayerPaidSubscription = await HasActivePaidBundleSubscriptionAsync(user.Id, BundleType.PlayerParent, cancellationToken);
            if (hasActivePlayerPaidSubscription)
            {
                return AccountTypeUpdateResult.Failed(
                    "Parent/player roles stay active while a paid player subscription is active. Cancel or downgrade player billing first, then remove those roles after billing ends.");
            }
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var publicRolesToRemove = currentRoles
            .Where(role => TryOutSpotRoles.PublicOrLegacyRegistrationRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (publicRolesToRemove.Length > 0)
        {
            var removeResult = await userManager.RemoveFromRolesAsync(user, publicRolesToRemove);
            if (!removeResult.Succeeded)
            {
                return AccountTypeUpdateResult.Failed(string.Join(" ", removeResult.Errors.Select(error => error.Description)));
            }
        }

        var addResult = await userManager.AddToRolesAsync(user, validatedAccountTypes);
        if (!addResult.Succeeded)
        {
            return AccountTypeUpdateResult.Failed(string.Join(" ", addResult.Errors.Select(error => error.Description)));
        }

        var now = DateTime.UtcNow;
        if (hasCurrentTeamRole && !hasRequestedTeamRole)
        {
            await SoftDeactivateTeamBundleDataAsync(user.Id, now, cancellationToken);
        }

        if (hasCurrentPlayerParentRole && !hasRequestedPlayerParentRole)
        {
            await SoftDeactivatePlayerBundleDataAsync(user.Id, now, cancellationToken);
        }

        user.UpdatedAt = now;
        await userManager.UpdateAsync(user);
        await transaction.CommitAsync(cancellationToken);
        await SignInWebUserAsync(user, isPersistent: true);
        return AccountTypeUpdateResult.Success("Account types updated.");
    }

    private async Task<bool> HasActivePaidBundleSubscriptionAsync(
        Guid userId,
        BundleType bundleType,
        CancellationToken cancellationToken)
    {
        var subscriptions = await dbContext.Subscriptions
            .AsNoTracking()
            .Where(subscription => subscription.UserId == userId)
            .Select(subscription => new
            {
                subscription.PlanType,
                subscription.Status
            })
            .ToArrayAsync(cancellationToken);

        foreach (var subscription in subscriptions
            .Where(subscription => TryOutSpotBillingCatalog.IsEntitlingSubscriptionStatus(subscription.Status)))
        {
            var normalizedPlanCode = TryOutSpotBillingCatalog.NormalizePlanCode(subscription.PlanType);
            if (normalizedPlanCode is null)
            {
                continue;
            }

            if (bundleType == BundleType.Team
                && (normalizedPlanCode == TryOutSpotPlanCodes.TeamBasic
                    || normalizedPlanCode == TryOutSpotPlanCodes.TeamProfessional
                    || normalizedPlanCode == TryOutSpotPlanCodes.EnterpriseOrganization))
            {
                return true;
            }

            if (bundleType == BundleType.PlayerParent
                && normalizedPlanCode == TryOutSpotPlanCodes.PremiumPlayer)
            {
                return true;
            }
        }

        var now = DateTime.UtcNow;
        var complimentaryGrantPlanCodes = bundleType == BundleType.Team
            ? new[]
            {
                TryOutSpotPlanCodes.TeamBasic,
                TryOutSpotPlanCodes.TeamProfessional,
                TryOutSpotPlanCodes.EnterpriseOrganization
            }
            : new[] { TryOutSpotPlanCodes.PremiumPlayer };

        return await dbContext.ComplimentaryPlanGrants
            .AsNoTracking()
            .AnyAsync(grant => grant.UserId == userId
                && complimentaryGrantPlanCodes.Contains(grant.PlanType)
                && grant.RevokedAt == null
                && grant.StartsAt <= now
                && (grant.EndsAt == null || grant.EndsAt > now),
                cancellationToken);
    }

    private async Task SoftDeactivateTeamBundleDataAsync(
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var teams = await dbContext.Teams
            .Where(team => team.UserTeamRoles.Any(role => role.UserId == userId))
            .ToArrayAsync(cancellationToken);
        if (teams.Length == 0)
        {
            return;
        }

        var teamIds = teams.Select(team => team.Id).ToArray();
        foreach (var team in teams)
        {
            team.IsActive = false;
            team.IsSearchable = false;
            team.IsContactInfoVisible = false;
            team.UpdatedAt = now;
        }

        var opportunities = await dbContext.Opportunities
            .Where(opportunity => teamIds.Contains(opportunity.TeamId))
            .Where(opportunity => opportunity.IsActive || opportunity.IsPublished)
            .ToArrayAsync(cancellationToken);
        foreach (var opportunity in opportunities)
        {
            opportunity.IsActive = false;
            opportunity.IsPublished = false;
            opportunity.ExpiresAt ??= now;
            opportunity.UpdatedAt = now;
        }
    }

    private async Task SoftDeactivatePlayerBundleDataAsync(
        Guid userId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var playerIds = await dbContext.UserPlayerRelationships
            .AsNoTracking()
            .Where(relationship => relationship.UserId == userId && relationship.CanManage)
            .Select(relationship => relationship.PlayerId)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (playerIds.Length == 0)
        {
            return;
        }

        var players = await dbContext.Players
            .Where(player => playerIds.Contains(player.Id))
            .ToArrayAsync(cancellationToken);
        foreach (var player in players)
        {
            player.IsActive = false;
            player.IsSearchable = false;
            player.UpdatedAt = now;
        }

        var listings = await dbContext.PlayerListings
            .Where(listing => listing.UserId == userId)
            .Where(listing => listing.PlayerId.HasValue && playerIds.Contains(listing.PlayerId.Value))
            .Where(listing => listing.IsActive || listing.IsPublished)
            .ToArrayAsync(cancellationToken);
        foreach (var listing in listings)
        {
            listing.IsActive = false;
            listing.IsPublished = false;
            listing.ExpiresAt ??= now;
            listing.UpdatedAt = now;
        }
    }

    private static bool IsActivePaidSubscription(Subscription subscription)
    {
        var plan = TryOutSpotBillingCatalog.GetPlan(subscription.PlanType);
        return plan?.RequiresStripeSubscription == true
            && TryOutSpotBillingCatalog.IsEntitlingSubscriptionStatus(subscription.Status);
    }

    private static string FormatCurrentPlanName(
        IReadOnlyCollection<Subscription> activePaidSubscriptions,
        IReadOnlyCollection<ComplimentaryPlanGrant> activeComplimentaryGrants)
    {
        var activeMembershipCount = activePaidSubscriptions.Count + activeComplimentaryGrants.Count;
        if (activeMembershipCount == 0)
        {
            return "Free Player/Parent";
        }

        if (activeMembershipCount == 1)
        {
            if (activePaidSubscriptions.Count == 1)
            {
                var subscription = activePaidSubscriptions.Single();
                return TryOutSpotBillingCatalog.GetPlan(subscription.PlanType)?.Name ?? subscription.PlanType;
            }

            var grant = activeComplimentaryGrants.Single();
            return TryOutSpotBillingCatalog.GetPlan(grant.PlanType)?.Name ?? grant.PlanType;
        }

        return $"{activeMembershipCount} active memberships";
    }

    private static string? FormatCurrentPlanStatus(
        IReadOnlyCollection<Subscription> activePaidSubscriptions,
        IReadOnlyCollection<ComplimentaryPlanGrant> activeComplimentaryGrants)
    {
        var activeMembershipCount = activePaidSubscriptions.Count + activeComplimentaryGrants.Count;
        if (activeMembershipCount == 0)
        {
            return null;
        }

        if (activeMembershipCount == 1)
        {
            return activePaidSubscriptions.Count == 1
                ? activePaidSubscriptions.Single().Status
                : "Complimentary";
        }

        var subscriptionPlanNames = activePaidSubscriptions
            .Select(subscription => TryOutSpotBillingCatalog.GetPlan(subscription.PlanType)?.Name ?? subscription.PlanType)
            .ToArray();
        var grantPlanNames = activeComplimentaryGrants
            .Select(grant => $"{TryOutSpotBillingCatalog.GetPlan(grant.PlanType)?.Name ?? grant.PlanType} complimentary")
            .ToArray();

        return string.Join(", ", subscriptionPlanNames.Concat(grantPlanNames).OrderBy(planName => planName));
    }

    private static IReadOnlyCollection<AccountMembershipSummaryItem> BuildMembershipSummaries(
        IReadOnlyCollection<Subscription> subscriptions,
        IReadOnlyCollection<ComplimentaryPlanGrant> complimentaryGrants,
        DateTime now)
    {
        var subscriptionSummaries = subscriptions
            .Select(subscription => new AccountMembershipSummaryProjection(
                ToMembershipSummaryItem(subscription),
                GetSubscriptionMembershipSummaryRank(subscription),
                subscription.UpdatedAt));
        var grantSummaries = complimentaryGrants
            .Select(grant => new AccountMembershipSummaryProjection(
                ToMembershipSummaryItem(grant, now),
                GetComplimentaryGrantMembershipSummaryRank(grant, now),
                grant.UpdatedAt));

        return subscriptionSummaries
            .Concat(grantSummaries)
            .OrderByDescending(summary => summary.Rank)
            .ThenByDescending(summary => summary.UpdatedAt)
            .Select(summary => summary.Item)
            .ToArray();
    }

    private static AccountMembershipSummaryItem ToMembershipSummaryItem(Subscription subscription)
    {
        var normalizedPlanCode = TryOutSpotBillingCatalog.NormalizePlanCode(subscription.PlanType);
        var plan = normalizedPlanCode is null
            ? null
            : TryOutSpotBillingCatalog.GetPlan(normalizedPlanCode);
        var hasActiveEntitlement = TryOutSpotBillingCatalog.IsEntitlingSubscriptionStatus(subscription.Status);

        return new AccountMembershipSummaryItem(
            plan?.Name ?? subscription.PlanType,
            FormatSubscriptionStatus(subscription.Status),
            FormatBillingIntervalDisplay(subscription.BillingInterval),
            FormatSubscriptionAmountDisplay(subscription, plan),
            FormatScopeDisplay(subscription.ScopeType),
            hasActiveEntitlement,
            subscription.CurrentPeriodStart,
            subscription.CurrentPeriodEnd,
            subscription.CancelAtPeriodEnd,
            subscription.UpdatedAt);
    }

    private static AccountMembershipSummaryItem ToMembershipSummaryItem(
        ComplimentaryPlanGrant grant,
        DateTime now)
    {
        var normalizedPlanCode = TryOutSpotBillingCatalog.NormalizePlanCode(grant.PlanType);
        var plan = normalizedPlanCode is null
            ? null
            : TryOutSpotBillingCatalog.GetPlan(normalizedPlanCode);
        var hasActiveEntitlement = ComplimentaryPlanGrantMapper.IsActive(grant, now);

        return new AccountMembershipSummaryItem(
            plan?.Name ?? grant.PlanType,
            FormatSubscriptionStatus(ComplimentaryPlanGrantMapper.GetStatus(grant, now)),
            "Complimentary",
            "Free",
            FormatScopeDisplay(grant.ScopeType),
            hasActiveEntitlement,
            grant.StartsAt,
            grant.EndsAt,
            false,
            grant.UpdatedAt)
        {
            CurrentPeriodEndLabel = "Access ends",
            ActiveEntitlementLabel = "Complimentary access"
        };
    }

    private static int GetSubscriptionMembershipSummaryRank(Subscription subscription)
    {
        var plan = TryOutSpotBillingCatalog.GetPlan(subscription.PlanType);
        var hasActiveEntitlement = TryOutSpotBillingCatalog.IsEntitlingSubscriptionStatus(subscription.Status);

        return hasActiveEntitlement switch
        {
            true when plan?.RequiresStripeSubscription == true => 4,
            true => 2,
            _ => 0
        };
    }

    private static int GetComplimentaryGrantMembershipSummaryRank(ComplimentaryPlanGrant grant, DateTime now)
    {
        if (ComplimentaryPlanGrantMapper.IsActive(grant, now))
        {
            return 3;
        }

        return grant.RevokedAt is null && grant.StartsAt > now ? 1 : 0;
    }

    private static string FormatSubscriptionStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "Unknown";
        }

        return status.Trim() switch
        {
            "active" => "Active",
            "trialing" => "Trialing",
            "past_due" => "Past due",
            "canceled" => "Canceled",
            "cancelled" => "Canceled",
            "incomplete" => "Incomplete",
            "incomplete_expired" => "Incomplete expired",
            "unpaid" => "Unpaid",
            "plan_selected" => "Plan selected",
            "checkout_started" => "Checkout started",
            var current => string.Join(' ', current.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]))
        };
    }

    private static string FormatBillingIntervalDisplay(string? billingInterval)
    {
        return BillingIntervalCodes.Normalize(billingInterval) switch
        {
            BillingIntervalCodes.Month => "Monthly",
            BillingIntervalCodes.Year => "Annual",
            _ => "N/A"
        };
    }

    private static string FormatSubscriptionAmountDisplay(Subscription subscription, BillingPlanDefinition? plan)
    {
        var requiresStripeSubscription = plan?.RequiresStripeSubscription == true;
        if (!requiresStripeSubscription)
        {
            return "Free";
        }

        var intervalSuffix = BillingIntervalCodes.Normalize(subscription.BillingInterval) switch
        {
            BillingIntervalCodes.Month => "/mo",
            BillingIntervalCodes.Year => "/yr",
            _ => string.Empty
        };

        if (subscription.Amount is { } amount && amount > 0)
        {
            return $"{amount:C2}{intervalSuffix}";
        }

        if (plan is not null)
        {
            var fallbackAmount = BillingIntervalCodes.Normalize(subscription.BillingInterval) switch
            {
                BillingIntervalCodes.Month => plan.MonthlyAmount,
                BillingIntervalCodes.Year => plan.AnnualAmount,
                _ => plan.MonthlyAmount
            };

            if (fallbackAmount is { } configuredAmount && configuredAmount > 0)
            {
                return $"{configuredAmount:C2}{intervalSuffix}";
            }
        }

        return "Paid (amount pending)";
    }

    private static string FormatScopeDisplay(string? scopeType)
    {
        return TryOutSpotSubscriptionScopeTypes.Normalize(scopeType) switch
        {
            TryOutSpotSubscriptionScopeTypes.Account => "Account",
            TryOutSpotSubscriptionScopeTypes.Player => "Player",
            TryOutSpotSubscriptionScopeTypes.Team => "Team",
            TryOutSpotSubscriptionScopeTypes.Organization => "Organization",
            _ => "Account"
        };
    }

    private static string TrimCheckoutSessionIdForDisplay(string sessionId)
    {
        var trimmed = sessionId.Trim();
        if (trimmed.Length <= 16)
        {
            return trimmed;
        }

        return $"{trimmed[..8]}...{trimmed[^6..]}";
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

    private static string? SerializeRegistrationFieldCodes(IReadOnlyCollection<string> requiredFieldCodes)
    {
        if (requiredFieldCodes.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(requiredFieldCodes);
    }

    private static IReadOnlyCollection<string> DeserializeRegistrationFieldCodes(string? serializedFieldCodes)
    {
        if (string.IsNullOrWhiteSpace(serializedFieldCodes))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(serializedFieldCodes);
            return TryOutSpotOpportunityRegistrationFields.NormalizeSelectedCodes(parsed);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool ResolveWaiverRequired(
        bool storedWaiverRequired,
        IReadOnlyCollection<string> requiredFieldCodes)
    {
        return storedWaiverRequired
            || requiredFieldCodes.Contains(TryOutSpotOpportunityRegistrationFields.WaiverSignature, StringComparer.OrdinalIgnoreCase);
    }

    private static string? ResolveWaiverMethod(
        string? storedWaiverMethod,
        bool waiverRequired)
    {
        if (!waiverRequired)
        {
            return null;
        }

        return TryOutSpotOpportunityWaiverMethods.Normalize(storedWaiverMethod)
            ?? TryOutSpotOpportunityWaiverMethods.AtEvent;
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

    private static string BuildCheckoutIdempotencyKey(
        Guid userId,
        string planCode,
        string billingInterval,
        string scopeType,
        Guid? scopeId)
    {
        var normalizedScopeId = scopeId?.ToString("N") ?? "account";
        var key = $"checkout:{userId:N}:{planCode}:{billingInterval}:{scopeType}:{normalizedScopeId}";
        return key.Length <= 255 ? key : key[..255];
    }

    private static string BuildCustomerPortalIdempotencyKey(Guid userId)
    {
        var key = $"portal:{userId:N}";
        return key.Length <= 255 ? key : key[..255];
    }

    private static string BuildCancelMembershipIdempotencyKey(Guid userId, string stripeSubscriptionId)
    {
        var key = $"cancel:{userId:N}:{stripeSubscriptionId.Trim()}";
        return key.Length <= 255 ? key : key[..255];
    }

    private static string FormatDisplayDate(DateTime value)
    {
        return value.ToLocalTime().ToString("MMM d, yyyy h:mm tt");
    }

    private static DateTime? NormalizeUtc(DateTime? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        var normalized = value.Value;
        return normalized.Kind switch
        {
            DateTimeKind.Utc => normalized,
            DateTimeKind.Local => normalized.ToUniversalTime(),
            _ => DateTime.SpecifyKind(normalized, DateTimeKind.Utc)
        };
    }

    private static DateTime NormalizeUtcDate(DateTime value)
    {
        return DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
    }

    private static string? NormalizeState(string? state)
    {
        return string.IsNullOrWhiteSpace(state) ? null : state.Trim().ToUpperInvariant();
    }

    private static string? NormalizeCurrency(string? currency)
    {
        if (string.IsNullOrWhiteSpace(currency))
        {
            return null;
        }

        var normalized = currency.Trim().ToUpperInvariant();
        return normalized.Length > 3 ? normalized[..3] : normalized;
    }

    private static bool IsUndefinedTableException(PostgresException exception)
    {
        return string.Equals(exception.SqlState, PostgresErrorCodes.UndefinedTable, StringComparison.Ordinal);
    }

    private sealed record TeamItemSearchProjection(
        Guid OpportunityId,
        Guid TeamId,
        string Title,
        string Type,
        string TeamName,
        string? OrganizationName,
        string SportName,
        string? Description,
        string? CompetitionLevel,
        string? AgeGroup,
        decimal RegistrationFee,
        DateTime? RegistrationDeadline,
        DateTime? EventDate,
        DateTime? EventEndDate,
        string? City,
        string? State,
        string? ZipCode,
        DateTime? PublishedAt,
        double? DistanceSort,
        bool IsFavorited,
        int RelevanceScore);

    private sealed record PlayerSearchProjection(
        Guid ListingId,
        string ListingType,
        string Title,
        string? Description,
        Guid? PlayerId,
        string? PlayerName,
        DateTime? PlayerDateOfBirth,
        string? SchoolName,
        string? CurrentTeamName,
        int? GraduationYear,
        string? Height,
        string? Weight,
        string? ThrowsHand,
        string? BatsHand,
        string? SportName,
        decimal? AskingPrice,
        string? Currency,
        string? Condition,
        string? City,
        string? State,
        string? ZipCode,
        DateTime? PublishedAt,
        double? DistanceSort,
        bool IsPriorityListing,
        bool IsFavorited,
        int RelevanceScore);

    private sealed record PlayerListingValidationContext(
        PlayerListingTypeSelectionPageItem? ListingTypeOption,
        Player? Player);

    private sealed record PlayerProfileValidationContext(
        string Relationship,
        string ContactVisibility,
        Guid[] SelectedSportIds,
        PlayerSportDetailPageModel[] SelectedSportDetails);

    private sealed record TeamPostingAccess(
        bool HasLimitedPosting,
        bool HasUnlimitedPosting,
        int PublishingLimit,
        int PublishingWindowMonths,
        string PlanLabel)
    {
        public bool CanPostOpportunities => HasLimitedPosting || HasUnlimitedPosting;

        public static TeamPostingAccess None { get; } = new(false, false, 0, 0, "No plan");
        public static TeamPostingAccess Professional { get; } = new(true, false, ProfessionalTeamPublishingLimit, ProfessionalTeamPublishingWindowMonths, "Professional Team");
        public static TeamPostingAccess Enterprise { get; } = new(true, false, EnterpriseTeamPublishingLimit, EnterpriseTeamPublishingWindowMonths, "Enterprise Organization");
        public static TeamPostingAccess Basic { get; } = new(true, false, BasicTeamPublishingLimit, BasicTeamPublishingWindowMonths, "Basic Team");
        public static TeamPostingAccess FreeCoach { get; } = new(true, false, FreeCoachPublishingLimit, FreeCoachPublishingWindowMonths, "Free Coach");
    }

    private sealed record TeamCreationAccess(bool CanCreateAdditionalTeam, string? BlockReason)
    {
        public static TeamCreationAccess Allow { get; } = new(true, null);

        public static TeamCreationAccess Blocked(string reason) => new(false, reason);
    }

    private sealed record ManagedTeamRoleContext(Team Team, string Role);
    private sealed record ListingRegistrationStats(
        int TotalCount,
        int UniqueApplicantCount,
        int PendingCount,
        int ApprovedCount,
        int DeclinedCount);

    private sealed record RegistrationDataSnapshot(
        DateTime? PlayerBirthDate,
        string? PlayerSchool,
        string? PlayerPhone,
        string? PlayerEmail,
        string? GuardianName,
        string? GuardianEmail,
        string? GuardianPhone,
        string? EmergencyContactName,
        string? EmergencyContactPhone,
        string? MedicalInfo,
        string? AdditionalNotes)
    {
        public static RegistrationDataSnapshot Empty { get; } = new(null, null, null, null, null, null, null, null, null, null, null);
    }

    private enum RegistrationStatusCategory
    {
        Pending,
        Approved,
        Declined
    }

    private sealed record UploadedImagePayload(string FileName, byte[] Content, string ContentType);

    private sealed record AccountMembershipSummaryProjection(
        AccountMembershipSummaryItem Item,
        int Rank,
        DateTime UpdatedAt);

    private enum BundleType
    {
        Team,
        PlayerParent
    }

    private sealed record AccountTypeUpdateResult(bool Updated, string Message)
    {
        public static AccountTypeUpdateResult Success(string message) => new(true, message);

        public static AccountTypeUpdateResult Failed(string message) => new(false, message);
    }
}
