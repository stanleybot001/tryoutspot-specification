using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Stripe;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Listings;
using TryOutSpot.Web.Models.Billing;
using TryOutSpot.Web.Security;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Tests;

public sealed class AccountPageTests
{
    [Fact]
    public async Task RegisterPage_RendersCompleteSignupForm()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/account/register");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Create your TryOutSpot account", html);
        Assert.Contains("name=\"Email\"", html);
        Assert.Contains("name=\"Password\"", html);
        Assert.Contains("name=\"ConfirmPassword\"", html);
        Assert.Contains("name=\"FirstName\"", html);
        Assert.Contains("name=\"LastName\"", html);
        Assert.Contains("name=\"AccountTypes\"", html);
        Assert.Contains("name=\"PhoneNumber\"", html);
        Assert.Contains("name=\"SmsConsentAccepted\"", html);
        Assert.Contains("I agree to receive transactional SMS messages", html);
        Assert.Contains("/privacy-policy", html);
        Assert.Contains("/terms-and-conditions", html);
        Assert.Contains("name=\"DateOfBirth\"", html);
        Assert.Contains("name=\"ZipCode\"", html);
        Assert.Contains("required-marker", html);
    }

    [Fact]
    public async Task RegisterPage_WithConfiguredGoogle_ShowsSocialSignupOption()
    {
        await using var factory = CreateFactoryWithGoogleConfiguration();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/account/register");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Continue with Google", html);
    }

    [Fact]
    public async Task LoginAndRecoveryPages_RenderRequiredFields()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();

        var loginResponse = await client.GetAsync("/account/login");
        var forgotPasswordResponse = await client.GetAsync("/account/forgot-password");
        var resetPasswordResponse = await client.GetAsync("/account/reset-password?email=test@example.com&token=abc");

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, forgotPasswordResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, resetPasswordResponse.StatusCode);

        var loginHtml = await loginResponse.Content.ReadAsStringAsync();
        var forgotPasswordHtml = await forgotPasswordResponse.Content.ReadAsStringAsync();
        var resetPasswordHtml = await resetPasswordResponse.Content.ReadAsStringAsync();

        Assert.Contains("name=\"Email\"", loginHtml);
        Assert.Contains("name=\"Password\"", loginHtml);
        Assert.Contains("Forgot password?", loginHtml);
        Assert.Contains("name=\"Email\"", forgotPasswordHtml);
        Assert.Contains("name=\"NewPassword\"", resetPasswordHtml);
        Assert.Contains("name=\"ConfirmNewPassword\"", resetPasswordHtml);
    }

    [Fact]
    public async Task RegisterConfirmationPage_ShowsSpamAndSafeSenderGuidance()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/account/register-confirmation?email=test@example.com");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Junk or Spam folder", html);
        Assert.Contains("Not Spam", html);
        Assert.Contains("no-reply@tryoutspot.com", html);
    }

    [Fact]
    public async Task RegisterPost_WithCompleteFields_CreatesUserAndRedirectsToConfirmation()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var email = $"web-register-{Guid.NewGuid():N}@example.com";
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/account/register");

        var response = await client.PostAsync(
            "/account/register",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", antiForgeryToken),
                new("Email", email),
                new("Password", "Tryout2026"),
                new("ConfirmPassword", "Tryout2026"),
                new("FirstName", "Morgan"),
                new("LastName", "Taylor"),
                new("AccountTypes", TryOutSpotRoles.Parent),
                new("AccountTypes", TryOutSpotRoles.TeamRepresentative),
                new("PhoneNumber", "555-555-9191"),
                new("SmsConsentAccepted", "true"),
                new("DateOfBirth", "2010-05-01"),
                new("ZipCode", "73102"),
                new("City", "Oklahoma City"),
                new("State", "OK")
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/account/register-confirmation", response.Headers.Location?.ToString());

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.Equal("Morgan", user.FirstName);
        Assert.Equal("Taylor", user.LastName);
        Assert.Equal("555-555-9191", user.PhoneNumber);
        Assert.True(user.SmsConsentAccepted);
        Assert.NotNull(user.SmsConsentAcceptedAt);
        Assert.Equal(TryOutSpotSmsConsent.CheckboxText, user.SmsConsentText);
        Assert.Equal(TryOutSpotSmsConsent.AccountRegistrationSource, user.SmsConsentSource);
        Assert.Equal("73102", user.ZipCode);
        Assert.Equal("OK", user.State);

        var roles = await userManager.GetRolesAsync(user);
        Assert.Contains(TryOutSpotRoles.Parent, roles);
        Assert.Contains(TryOutSpotRoles.TeamRepresentative, roles);
    }

    [Fact]
    public async Task RegisterConfirmationPost_ResendsVerificationEmail_ForUnverifiedAccount()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var email = $"resend-web-{Guid.NewGuid():N}@example.com";
        await factory.CreateUserAsync(email, [TryOutSpotRoles.Parent], emailConfirmed: false);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var antiForgeryToken = await GetAntiForgeryTokenAsync(
            client,
            $"/account/register-confirmation?email={Uri.EscapeDataString(email)}");

        var response = await client.PostAsync(
            "/account/resend-verification",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", antiForgeryToken),
                new("email", email)
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/account/register-confirmation", response.Headers.Location?.ToString());

        var emailSender = factory.Services.GetRequiredService<TestAccountEmailSender>();
        Assert.True(emailSender.TryGetEmailConfirmationToken(email, out _));
    }

    [Fact]
    public async Task LoginPost_WithUnverifiedEmail_ShowsResendVerificationPrompt()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var email = $"login-unverified-{Guid.NewGuid():N}@example.com";
        await factory.CreateUserAsync(email, [TryOutSpotRoles.Parent], emailConfirmed: false);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/account/login");
        var response = await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", antiForgeryToken),
                new("Email", email),
                new("Password", "Tryout2026"),
                new("RememberMe", "true")
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Email verification is required before login.", html);
        Assert.Contains("Need a new verification email?", html);
        Assert.Contains("/account/login/resend-verification", html);
    }

    [Fact]
    public async Task LoginResendVerificationPost_ResendsVerificationEmail_ForUnverifiedAccount()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var email = $"resend-login-{Guid.NewGuid():N}@example.com";
        await factory.CreateUserAsync(email, [TryOutSpotRoles.Parent], emailConfirmed: false);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/account/login");
        var response = await client.PostAsync(
            "/account/login/resend-verification",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", antiForgeryToken),
                new("email", email),
                new("returnUrl", "/account/onboarding")
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/account/login", response.Headers.Location?.ToString());

        var emailSender = factory.Services.GetRequiredService<TestAccountEmailSender>();
        Assert.True(emailSender.TryGetEmailConfirmationToken(email, out _));
    }

    [Fact]
    public async Task Onboarding_RequiresWebCookieAndRendersAfterLogin()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        await factory.CreateUserAsync("web-onboarding@example.com", [TryOutSpotRoles.Parent]);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/account/login");

        var anonymousResponse = await client.GetAsync("/account/onboarding");
        Assert.Equal(HttpStatusCode.Redirect, anonymousResponse.StatusCode);
        Assert.Contains("/account/login", anonymousResponse.Headers.Location?.ToString());

        var loginResponse = await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", antiForgeryToken),
                new("Email", "web-onboarding@example.com"),
                new("Password", "Tryout2026"),
                new("RememberMe", "true")
            ]));

        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
        Assert.Equal("/account/onboarding", loginResponse.Headers.Location?.ToString());

        var onboardingResponse = await client.GetAsync("/account/onboarding");
        Assert.Equal(HttpStatusCode.OK, onboardingResponse.StatusCode);

        var html = await onboardingResponse.Content.ReadAsStringAsync();
        Assert.Contains("Setup checklist", html);
        Assert.DoesNotContain("name=\"playerParentRole\"", html);
        Assert.DoesNotContain("name=\"teamRole\"", html);
        Assert.Contains("Completed setup", html);
        Assert.Contains("Recommended plan options", html);
        Assert.Contains("href=\"/account/onboarding/add-player-profile\"", html);
        Assert.Contains("href=\"/account/onboarding/choose-plan\"", html);
    }

    [Fact]
    public async Task Onboarding_WithTeamRepresentativeRole_ShowsTeamSetupAndPlanSteps()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        await factory.CreateUserAsync("web-onboarding-coach@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        await LoginWebUserAsync(client, "web-onboarding-coach@example.com");
        var response = await client.GetAsync("/account/onboarding");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("href=\"/account/onboarding/add-team-or-organization\"", html);
        Assert.Contains("href=\"/account/onboarding/choose-plan\"", html);
    }

    [Fact]
    public async Task Onboarding_WithManagedPlayerTryoutRegistration_ShowsUpcomingRegistrationList()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("onboarding-registrations@example.com", [TryOutSpotRoles.Parent]);

        Guid opportunityId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;
            var sportId = await dbContext.Sports
                .Where(sport => sport.IsActive && sport.Name == "Softball")
                .Select(sport => sport.Id)
                .SingleAsync();

            var teamId = Guid.NewGuid();
            dbContext.Teams.Add(new Team
            {
                Id = teamId,
                Name = "Dashboard Team",
                TeamLevel = "12U",
                GeographicScope = "Regional",
                City = "McPherson",
                State = "KS",
                ZipCode = "67460",
                IsSearchable = true,
                IsContactInfoVisible = true,
                IsElite = false,
                IsVerified = false,
                CreatedAt = now,
                UpdatedAt = now,
                IsActive = true
            });

            var playerId = Guid.NewGuid();
            dbContext.Players.Add(new Player
            {
                Id = playerId,
                FirstName = "Mia",
                LastName = "Jordan",
                DateOfBirth = new DateTime(2012, 6, 12, 0, 0, 0, DateTimeKind.Utc),
                ContactVisibility = "VerifiedCoachesOnly",
                IsSearchable = true,
                CreatedAt = now,
                UpdatedAt = now,
                IsActive = true
            });
            dbContext.UserPlayerRelationships.Add(new UserPlayerRelationship
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                PlayerId = playerId,
                Relationship = "Parent",
                CanManage = true,
                CreatedAt = now
            });

            opportunityId = Guid.NewGuid();
            dbContext.Opportunities.Add(new Opportunity
            {
                Id = opportunityId,
                TeamId = teamId,
                SportId = sportId,
                Type = "tryout",
                Title = "June Exposure Tryout",
                RegistrationRequired = true,
                RegistrationFee = 25m,
                EventDate = now.AddDays(10),
                EventEndDate = now.AddDays(11),
                City = "McPherson",
                State = "KS",
                IsPublished = true,
                PublishedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                IsActive = true
            });

            dbContext.Registrations.Add(new Registration
            {
                Id = Guid.NewGuid(),
                OpportunityId = opportunityId,
                PlayerId = playerId,
                RegisteredByUserId = user.Id,
                Status = "pending",
                RegistrationData = "{}",
                PaymentStatus = "in_person",
                Amount = 25m,
                WaiverSigned = false,
                AttendanceStatus = "pending",
                CreatedAt = now,
                UpdatedAt = now
            });

            await dbContext.SaveChangesAsync();
        }

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, user.Email!);
        var response = await client.GetAsync("/account/onboarding");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Upcoming tryout registrations", html);
        Assert.Contains("June Exposure Tryout", html);
        Assert.Contains("Mia Jordan", html);
        Assert.Contains("Pending review", html);
        Assert.Contains($"/opportunities/{opportunityId}", html);
    }

    [Fact]
    public async Task Settings_RequiresWebCookieAndRendersAccountManagementForms()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        await factory.CreateUserAsync("settings-page@example.com", [TryOutSpotRoles.Parent, TryOutSpotRoles.TeamRepresentative]);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var anonymousResponse = await client.GetAsync("/account/settings");
        Assert.Equal(HttpStatusCode.Redirect, anonymousResponse.StatusCode);
        Assert.Contains("/account/login", anonymousResponse.Headers.Location?.ToString());

        await LoginWebUserAsync(client, "settings-page@example.com");

        var response = await client.GetAsync("/account/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Account settings", html);
        Assert.Contains("name=\"Profile.FirstName\"", html);
        Assert.Contains("name=\"Email.NewEmail\"", html);
        Assert.Contains("name=\"Phone.PhoneNumber\"", html);
        Assert.Contains("name=\"Password.NewPassword\"", html);
        Assert.Contains("name=\"playerParentRole\"", html);
        Assert.Contains("name=\"teamRole\"", html);
        Assert.Contains("name=\"SmsConsent.SmsConsentAccepted\"", html);
        Assert.Contains("Membership access", html);
        Assert.Contains("Choose or change plan", html);
    }

    [Fact]
    public async Task Logout_PostWithoutAntiforgeryToken_SignsOutAndRedirectsToLogin()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        await factory.CreateUserAsync("logout-page@example.com", [TryOutSpotRoles.Parent]);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        await LoginWebUserAsync(client, "logout-page@example.com");

        var logoutResponse = await client.PostAsync("/account/logout", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.Redirect, logoutResponse.StatusCode);
        Assert.Equal("/account/login", logoutResponse.Headers.Location?.ToString());

        var onboardingResponse = await client.GetAsync("/account/onboarding");
        Assert.Equal(HttpStatusCode.Redirect, onboardingResponse.StatusCode);
        Assert.Contains("/account/login", onboardingResponse.Headers.Location?.ToString());
    }

    [Fact]
    public async Task SettingsPost_ProfileAccountTypesAndSmsConsent_UpdateCurrentUser()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("settings-update@example.com", [TryOutSpotRoles.Parent]);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, "settings-update@example.com");

        var profileToken = await GetAntiForgeryTokenAsync(client, "/account/settings");
        var profileResponse = await client.PostAsync(
            "/account/settings/profile",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", profileToken),
                new("Profile.FirstName", "Jordan"),
                new("Profile.LastName", "Casey"),
                new("Profile.DateOfBirth", "2011-04-03"),
                new("Profile.ZipCode", "66213"),
                new("Profile.City", "Overland Park"),
                new("Profile.State", "ks")
            ]));
        Assert.Equal(HttpStatusCode.Redirect, profileResponse.StatusCode);

        var accountTypesToken = await GetAntiForgeryTokenAsync(client, "/account/settings");
        var accountTypesResponse = await client.PostAsync(
            "/account/settings/account-types",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", accountTypesToken),
                new("accountTypes", TryOutSpotRoles.Player),
                new("accountTypes", TryOutSpotRoles.TeamRepresentative)
            ]));
        Assert.Equal(HttpStatusCode.Redirect, accountTypesResponse.StatusCode);

        var smsToken = await GetAntiForgeryTokenAsync(client, "/account/settings");
        var smsResponse = await client.PostAsync(
            "/account/settings/sms-consent",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", smsToken),
                new("SmsConsent.PhoneNumber", "620-555-1212"),
                new("SmsConsent.SmsConsentAccepted", "true")
            ]));
        Assert.Equal(HttpStatusCode.Redirect, smsResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var updatedUser = await userManager.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(updatedUser);
        Assert.Equal("Jordan", updatedUser.FirstName);
        Assert.Equal("Casey", updatedUser.LastName);
        Assert.Equal("66213", updatedUser.ZipCode);
        Assert.Equal("KS", updatedUser.State);
        Assert.Equal("620-555-1212", updatedUser.PhoneNumber);
        Assert.True(updatedUser.SmsConsentAccepted);
        Assert.Equal(TryOutSpotSmsConsent.AccountSettingsSource, updatedUser.SmsConsentSource);

        var roles = await userManager.GetRolesAsync(updatedUser);
        Assert.DoesNotContain(TryOutSpotRoles.Parent, roles);
        Assert.Contains(TryOutSpotRoles.Player, roles);
        Assert.Contains(TryOutSpotRoles.TeamRepresentative, roles);
    }

    [Fact]
    public async Task SettingsPost_EmailPhoneAndPasswordFlows_UpdateCurrentUser()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("settings-security@example.com", [TryOutSpotRoles.Parent]);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, "settings-security@example.com");

        var newEmail = $"settings-security-{Guid.NewGuid():N}@example.com";
        var emailToken = await GetAntiForgeryTokenAsync(client, "/account/settings");
        var emailResponse = await client.PostAsync(
            "/account/settings/email",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", emailToken),
                new("Email.NewEmail", newEmail),
                new("Email.CurrentPassword", "Tryout2026")
            ]));
        Assert.Equal(HttpStatusCode.Redirect, emailResponse.StatusCode);

        var emailSender = factory.Services.GetRequiredService<TestAccountEmailSender>();
        Assert.True(emailSender.TryGetEmailChangeToken(newEmail, out var changeEmailToken));

        var confirmResponse = await client.GetAsync(
            $"/account/confirm-email-change?userId={user.Id}&email={Uri.EscapeDataString(newEmail)}&token={Uri.EscapeDataString(changeEmailToken)}");
        Assert.Equal(HttpStatusCode.Redirect, confirmResponse.StatusCode);

        var sendPhoneToken = await GetAntiForgeryTokenAsync(client, "/account/settings");
        var sendPhoneResponse = await client.PostAsync(
            "/account/settings/send-phone-code",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", sendPhoneToken),
                new("Phone.PhoneNumber", "620-555-3434")
            ]));
        Assert.Equal(HttpStatusCode.Redirect, sendPhoneResponse.StatusCode);

        var smsSender = factory.Services.GetRequiredService<TestAccountSmsSender>();
        Assert.True(smsSender.TryGetCode("620-555-3434", out var phoneCode));

        var verifyPhoneToken = await GetAntiForgeryTokenAsync(client, "/account/settings");
        var verifyPhoneResponse = await client.PostAsync(
            "/account/settings/verify-phone",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", verifyPhoneToken),
                new("Phone.PhoneNumber", "620-555-3434"),
                new("Phone.VerificationCode", phoneCode)
            ]));
        Assert.Equal(HttpStatusCode.Redirect, verifyPhoneResponse.StatusCode);

        var passwordToken = await GetAntiForgeryTokenAsync(client, "/account/settings");
        var passwordResponse = await client.PostAsync(
            "/account/settings/password",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", passwordToken),
                new("Password.CurrentPassword", "Tryout2026"),
                new("Password.NewPassword", "Tryout2027"),
                new("Password.ConfirmNewPassword", "Tryout2027")
            ]));
        Assert.Equal(HttpStatusCode.Redirect, passwordResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var updatedUser = await userManager.FindByIdAsync(user.Id.ToString());
        Assert.NotNull(updatedUser);
        Assert.Equal(newEmail, updatedUser.Email);
        Assert.True(updatedUser.EmailConfirmed);
        Assert.Equal("620-555-3434", updatedUser.PhoneNumber);
        Assert.True(updatedUser.PhoneNumberConfirmed);
        Assert.True(await userManager.CheckPasswordAsync(updatedUser, "Tryout2027"));
    }

    [Fact]
    public async Task AddAndEditPlayerProfilePages_GroupInputsBySection()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("onboarding-player-profile-sections@example.com", [TryOutSpotRoles.Parent]);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, user.Email!);

        var createResponse = await client.GetAsync("/account/onboarding/add-player-profile?createNew=true");

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var createHtml = await createResponse.Content.ReadAsStringAsync();
        Assert.Contains("Add player profile", createHtml);
        AssertPlayerProfileSections(createHtml);

        var playerId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var now = DateTime.UtcNow;

            dbContext.Players.Add(new Player
            {
                Id = playerId,
                FirstName = "Alex",
                LastName = "Rivera",
                DateOfBirth = new DateTime(2011, 3, 14, 0, 0, 0, DateTimeKind.Utc),
                ContactVisibility = "VerifiedCoachesOnly",
                IsSearchable = true,
                CreatedAt = now,
                UpdatedAt = now,
                IsActive = true
            });
            dbContext.UserPlayerRelationships.Add(new UserPlayerRelationship
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                PlayerId = playerId,
                Relationship = "Parent",
                CanManage = true,
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync();
        }

        var editResponse = await client.GetAsync($"/account/onboarding/player-profiles/{playerId}/edit");

        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
        var editHtml = await editResponse.Content.ReadAsStringAsync();
        Assert.Contains("Edit player profile", editHtml);
        AssertPlayerProfileSections(editHtml);
    }

    [Fact]
    public async Task AddPlayerProfilePage_AndPost_CreateLinkedPlayerProfile()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("onboarding-player-profile@example.com", [TryOutSpotRoles.Parent]);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, user.Email!);

        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/account/onboarding/add-player-profile");

        Guid sportId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            sportId = await dbContext.Sports
                .Where(sport => sport.IsActive && sport.Name == "Softball")
                .Select(sport => sport.Id)
                .SingleAsync();
        }

        var response = await client.PostAsync(
            "/account/onboarding/add-player-profile",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", antiForgeryToken),
                new("FirstName", "Alex"),
                new("LastName", "Rivera"),
                new("DateOfBirth", "2011-03-14"),
                new("Relationship", "Parent"),
                new("CanManage", "true"),
                new("IsSearchable", "false"),
                new("ContactEmail", "parent-contact@example.com"),
                new("ContactPhone", "620-555-7070"),
                new("ProfileImageUrl", "https://cdn.example.com/player/alex.jpg"),
                new("HighlightVideoUrl1", "https://www.youtube.com/watch?v=alex123"),
                new("HighlightVideoUrl2", "https://www.hudl.com/video/alex456"),
                new("SchoolName", "Wichita Central High"),
                new("CurrentTeamName", "Midamserv 14U Select"),
                new("GraduationYear", "2029"),
                new("Height", "5'8\""),
                new("Weight", "145 lb"),
                new("ThrowsHand", "Right"),
                new("BatsHand", "Left"),
                new("SixtyYardDash", "6.9 sec"),
                new("HomeToFirstTime", "4.2 sec"),
                new("ExitVelocity", "82 mph"),
                new("ThrowingVelocity", "76 mph"),
                new("PitchVelocity", "72 mph"),
                new("CatcherPopTime", "1.95 sec"),
                new("AdditionalMetrics", "Strong first-step quickness."),
                new("ContactVisibility", "VerifiedCoachesOnly"),
                new("City", "Wichita"),
                new("State", "ks"),
                new("ZipCode", "67202"),
                new("FacebookPageUrl", "https://facebook.com/alex"),
                new("XPageUrl", "@alex"),
                new("InstagramUrl", "alex.ig"),
                new("YouTubeUrl", "https://youtube.com/@alex"),
                new("TikTokUrl", "@alextok"),
                new("SportsRecruitsProfileUrl", "https://sportsrecruits.com/athlete/alex"),
                new("FieldLevelProfileUrl", "https://fieldlevel.com/alex"),
                new("NcsaProfileUrl", "https://recruit-match.ncsasports.org/alex"),
                new("OtherRecruitingProfileUrl", "https://example.com/alex-recruiting"),
                new("SportDetails[0].SportId", sportId.ToString()),
                new("SportDetails[0].SportName", "Softball"),
                new("SportDetails[0].IsSelected", "true"),
                new("SportDetails[0].SkillLevel", "A"),
                new("SportDetails[0].PrimaryPosition", "Pitcher"),
                new("SportDetails[0].SecondaryPositions", "Shortstop"),
                new("SportDetails[0].ExperienceLevel", "Travel"),
                new("SportDetails[0].YearsPlaying", "6"),
                new("SportDetails[0].Availability", "Fall weekends")
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/onboarding/player-profiles", response.Headers.Location?.ToString());

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var player = await verifyDb.Players.SingleAsync();
        var relationship = await verifyDb.UserPlayerRelationships.SingleAsync();
        var playerSport = await verifyDb.PlayerSports.SingleAsync();

        Assert.Equal("Alex", player.FirstName);
        Assert.Equal("Rivera", player.LastName);
        Assert.Equal(new DateTime(2011, 3, 14, 0, 0, 0, DateTimeKind.Utc), player.DateOfBirth);
        Assert.Equal("KS", player.State);
        Assert.Equal("67202", player.ZipCode);
        Assert.Equal("https://cdn.example.com/player/alex.jpg", player.ProfileImageUrl);
        Assert.False(player.IsSearchable);
        Assert.Equal("Wichita Central High", player.SchoolName);
        Assert.Equal("Midamserv 14U Select", player.CurrentTeamName);
        Assert.Equal(2029, player.GraduationYear);
        Assert.Equal("5'8\"", player.Height);
        Assert.Equal("145 lb", player.Weight);
        Assert.Equal("Right", player.ThrowsHand);
        Assert.Equal("Left", player.BatsHand);
        Assert.Equal("6.9 sec", player.SixtyYardDash);
        Assert.Equal("4.2 sec", player.HomeToFirstTime);
        Assert.Equal("82 mph", player.ExitVelocity);
        Assert.Equal("76 mph", player.ThrowingVelocity);
        Assert.Equal("72 mph", player.PitchVelocity);
        Assert.Equal("1.95 sec", player.CatcherPopTime);
        Assert.Equal("Strong first-step quickness.", player.AdditionalMetrics);
        Assert.Equal("VerifiedCoachesOnly", player.ContactVisibility);
        Assert.Equal(user.Id, relationship.UserId);
        Assert.Equal(player.Id, relationship.PlayerId);
        Assert.Equal("Parent", relationship.Relationship);
        Assert.Equal(player.Id, playerSport.PlayerId);
        Assert.Equal(sportId, playerSport.SportId);
        Assert.Equal("A", playerSport.SkillLevel);
        Assert.Equal("Pitcher", playerSport.PrimaryPosition);
        Assert.Equal("Shortstop", playerSport.SecondaryPositions);
        Assert.Equal("Travel", playerSport.ExperienceLevel);
        Assert.Equal(6, playerSport.YearsPlaying);
        Assert.Equal("Fall weekends", playerSport.Availability);

        var socialMediaLinks = JsonSerializer.Deserialize<Dictionary<string, string>>(player.SocialMediaLinks ?? "{}");
        Assert.Equal("https://facebook.com/alex", socialMediaLinks?["facebook"]);
        Assert.Equal("https://x.com/alex", socialMediaLinks?["x"]);
        Assert.Equal("https://instagram.com/alex.ig", socialMediaLinks?["instagram"]);
        Assert.Equal("https://youtube.com/@alex", socialMediaLinks?["youtube"]);
        Assert.Equal("https://tiktok.com/alextok", socialMediaLinks?["tiktok"]);
        Assert.Equal("https://www.youtube.com/watch?v=alex123", socialMediaLinks?["highlight_video_1"]);
        Assert.Equal("https://www.hudl.com/video/alex456", socialMediaLinks?["highlight_video_2"]);

        var recruitingProfileLinks = JsonSerializer.Deserialize<Dictionary<string, string>>(player.RecruitingProfileLinks ?? "{}");
        Assert.Equal("https://sportsrecruits.com/athlete/alex", recruitingProfileLinks?["sportsrecruits"]);
        Assert.Equal("https://fieldlevel.com/alex", recruitingProfileLinks?["fieldlevel"]);
        Assert.Equal("https://recruit-match.ncsasports.org/alex", recruitingProfileLinks?["ncsa"]);
        Assert.Equal("https://example.com/alex-recruiting", recruitingProfileLinks?["other"]);
    }

    [Fact]
    public async Task PlayerListingDetail_FreeProfile_ShowsBasicFieldsAndHidesEnhancedFields()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var owner = await factory.CreateUserAsync("listing-detail-free-owner@example.com", [TryOutSpotRoles.Parent]);
        var listingId = SeedPlayerListingDetail(factory, owner.Id, "Public");
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/player-listings/{listingId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Birthday", html);
        Assert.Contains("Apr 5, 2011", html);
        Assert.Contains("Player location", html);
        Assert.Contains("Wichita, KS, 67202", html);
        Assert.Contains("Wichita Central High", html);
        Assert.Contains("Aces 16U", html);
        Assert.Contains("2027", html);
        Assert.Contains("5'9\" / 185 lb", html);
        Assert.Contains("Right", html);
        Assert.Contains("Left", html);
        Assert.Contains("Softball", html);
        Assert.DoesNotContain("Not set", html);
        Assert.DoesNotContain("Social pages", html);
        Assert.DoesNotContain("Profile videos", html);
        Assert.DoesNotContain("Recruiting profiles", html);
        Assert.DoesNotContain("Level: Advanced", html);
        Assert.DoesNotContain("Primary position: Pitcher", html);
        Assert.DoesNotContain("60-yard dash", html);
        Assert.DoesNotContain("6.9 sec", html);
        Assert.DoesNotContain("Experience: Varsity", html);
        Assert.DoesNotContain("SportsRecruits", html);
    }

    [Fact]
    public async Task PlayerListingDetail_PremiumProfile_ShowsEnhancedProfileDetails()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var owner = await factory.CreateUserAsync("listing-detail-premium-owner@example.com", [TryOutSpotRoles.Parent]);
        await AddSubscriptionAsync(factory, owner.Id, TryOutSpotPlanCodes.PremiumPlayer, "active");
        var listingId = SeedPlayerListingDetail(factory, owner.Id, "Public");
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/player-listings/{listingId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Wichita Central High", html);
        Assert.Contains("Social pages", html);
        Assert.Contains("Profile videos", html);
        Assert.Contains("Recruiting profiles", html);
        Assert.Contains("Level: Advanced", html);
        Assert.Contains("Primary position: Pitcher", html);
        Assert.Contains("Secondary positions: Shortstop", html);
        Assert.Contains("Experience: Varsity", html);
        Assert.Contains("Years playing: 8", html);
        Assert.Contains("Availability: Fall 2026 weekends", html);
        Assert.Contains("Performance metrics", html);
        Assert.Contains("60-yard dash", html);
        Assert.Contains("6.9 sec", html);
        Assert.Contains("Exit velocity", html);
        Assert.Contains("82 mph", html);
        Assert.Contains("Strong first-step quickness.", html);
        Assert.Contains("SportsRecruits", html);
        Assert.Contains("https://youtube.com/watch?v=alex-highlight", html);
    }

    [Fact]
    public async Task PlayerListingDetail_CoachOnlyContact_ShowsOnlyForTeamRepresentatives()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var owner = await factory.CreateUserAsync("listing-detail-contact-owner@example.com", [TryOutSpotRoles.Parent]);
        var teamUser = await factory.CreateUserAsync("listing-detail-contact-team@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var listingId = SeedPlayerListingDetail(factory, owner.Id, "VerifiedCoachesOnly");

        var publicResponse = await factory.CreateClient().GetAsync($"/player-listings/{listingId}");

        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        var publicHtml = WebUtility.HtmlDecode(await publicResponse.Content.ReadAsStringAsync());
        Assert.Contains("Contact details are limited to approved coach access", publicHtml);
        Assert.DoesNotContain("player-contact@example.com", publicHtml);

        var teamClient = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(teamClient, teamUser.Email!);

        var teamResponse = await teamClient.GetAsync($"/player-listings/{listingId}");

        Assert.Equal(HttpStatusCode.OK, teamResponse.StatusCode);
        var teamHtml = WebUtility.HtmlDecode(await teamResponse.Content.ReadAsStringAsync());
        Assert.Contains("player-contact@example.com", teamHtml);
        Assert.DoesNotContain("Contact details are limited to approved coach access", teamHtml);
        Assert.DoesNotContain("<span>Phone</span>", teamHtml);
    }

    [Fact]
    public async Task AddTeamOrOrganizationPage_AndPost_CreateLinkedTeamAndOrganizationRecords()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("onboarding-team-setup@example.com", [TryOutSpotRoles.TeamRepresentative]);
        await AddSubscriptionAsync(factory, user.Id, TryOutSpotPlanCodes.TeamBasic, "trialing");

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, user.Email!);

        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/account/onboarding/add-team-or-organization");

        Guid sportId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            sportId = await dbContext.Sports
                .Where(sport => sport.IsActive && sport.Name == "Baseball")
                .Select(sport => sport.Id)
                .SingleAsync();
        }

        var response = await client.PostAsync(
            "/account/onboarding/add-team-or-organization",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", antiForgeryToken),
                new("CreateType", "organization"),
                new("TeamName", "Midamserv Thunder 14U"),
                new("OrganizationName", "Midamserv Baseball Club"),
                new("TeamRole", TryOutSpotRoles.TeamRepresentative),
                new("TeamLevel", "14U"),
                new("GeographicScope", "Regional"),
                new("IsSearchable", "false"),
                new("ContactEmail", "coach@example.com"),
                new("ContactPhone", "620-555-9090"),
                new("ProfileImageUrl", "https://cdn.example.com/teams/thunder-logo.png"),
                new("HighlightVideoUrl1", "https://www.youtube.com/watch?v=thunder123"),
                new("HighlightVideoUrl2", "https://www.hudl.com/video/thunder456"),
                new("WebsiteUrl", "https://midamserv.test"),
                new("City", "Wichita"),
                new("State", "ks"),
                new("ZipCode", "67202"),
                new("FacebookPageUrl", "https://facebook.com/midamserv"),
                new("XPageUrl", "@midamserv"),
                new("InstagramUrl", "midamserv"),
                new("YouTubeUrl", "https://youtube.com/@midamserv"),
                new("TikTokUrl", "@midamserv"),
                new("GameChangerCoachName", "Coach Whitfield"),
                new("GameChangerTeamName", "Midamserv Thunder 14U"),
                new("SelectedSportIds", sportId.ToString())
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/onboarding", response.Headers.Location?.ToString());

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var organization = await verifyDb.Organizations.SingleAsync();
        var team = await verifyDb.Teams.SingleAsync();
        var userTeamRole = await verifyDb.UserTeamRoles.SingleAsync();
        var teamSport = await verifyDb.TeamSports.SingleAsync();

        Assert.Equal("Midamserv Baseball Club", organization.Name);
        Assert.False(organization.IsAcademy);
        Assert.False(organization.IsSearchable);
        Assert.Equal("Wichita", organization.City);
        Assert.Equal("KS", organization.State);
        Assert.Equal("https://cdn.example.com/teams/thunder-logo.png", organization.LogoImageUrl);
        Assert.Equal("Midamserv Thunder 14U", team.Name);
        Assert.Equal(organization.Id, team.OrganizationId);
        Assert.Equal("14U", team.TeamLevel);
        Assert.Equal("Regional", team.GeographicScope);
        Assert.Equal("KS", team.State);
        Assert.Equal("https://cdn.example.com/teams/thunder-logo.png", team.LogoImageUrl);
        Assert.False(team.IsSearchable);
        Assert.Equal(user.Id, userTeamRole.UserId);
        Assert.Equal(team.Id, userTeamRole.TeamId);
        Assert.Equal(TryOutSpotRoles.TeamRepresentative, userTeamRole.Role);
        Assert.Equal(team.Id, teamSport.TeamId);
        Assert.Equal(sportId, teamSport.SportId);

        var teamSocialLinks = JsonSerializer.Deserialize<Dictionary<string, string>>(team.SocialMediaLinks ?? "{}");
        Assert.Equal("https://facebook.com/midamserv", teamSocialLinks?["facebook"]);
        Assert.Equal("https://x.com/midamserv", teamSocialLinks?["x"]);
        Assert.Equal("https://instagram.com/midamserv", teamSocialLinks?["instagram"]);
        Assert.Equal("https://youtube.com/@midamserv", teamSocialLinks?["youtube"]);
        Assert.Equal("https://tiktok.com/midamserv", teamSocialLinks?["tiktok"]);
        Assert.Equal("Coach Whitfield", teamSocialLinks?["gamechanger_coach"]);
        Assert.Equal("Midamserv Thunder 14U", teamSocialLinks?["gamechanger_team_name"]);
        Assert.Equal("https://www.youtube.com/watch?v=thunder123", teamSocialLinks?["highlight_video_1"]);
        Assert.Equal("https://www.hudl.com/video/thunder456", teamSocialLinks?["highlight_video_2"]);

        var organizationSocialLinks = JsonSerializer.Deserialize<Dictionary<string, string>>(organization.SocialMediaLinks ?? "{}");
        Assert.Equal("https://facebook.com/midamserv", organizationSocialLinks?["facebook"]);
        Assert.Equal("https://x.com/midamserv", organizationSocialLinks?["x"]);
        Assert.Equal("https://instagram.com/midamserv", organizationSocialLinks?["instagram"]);
        Assert.Equal("https://youtube.com/@midamserv", organizationSocialLinks?["youtube"]);
        Assert.Equal("https://tiktok.com/midamserv", organizationSocialLinks?["tiktok"]);
        Assert.Equal("Coach Whitfield", organizationSocialLinks?["gamechanger_coach"]);
        Assert.Equal("Midamserv Thunder 14U", organizationSocialLinks?["gamechanger_team_name"]);
        Assert.Equal("https://www.youtube.com/watch?v=thunder123", organizationSocialLinks?["highlight_video_1"]);
        Assert.Equal("https://www.hudl.com/video/thunder456", organizationSocialLinks?["highlight_video_2"]);
    }

    [Fact]
    public async Task ChoosePlanPost_WithFreePlan_SavesSelectionAndReturnsToOnboarding()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("onboarding-plan-select@example.com", [TryOutSpotRoles.Parent]);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, user.Email!);

        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/account/onboarding/choose-plan");
        var response = await client.PostAsync(
            "/account/onboarding/choose-plan",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", antiForgeryToken),
                new("PlanCode", TryOutSpotPlanCodes.FreePlayerParent),
                new("BillingInterval", BillingIntervalCodes.Month)
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/onboarding", response.Headers.Location?.ToString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscription = await dbContext.Subscriptions.SingleAsync(current => current.UserId == user.Id);
        Assert.Equal(TryOutSpotPlanCodes.FreePlayerParent, subscription.PlanType);
        Assert.Equal("active", subscription.Status);
        Assert.Equal(BillingIntervalCodes.Month, subscription.BillingInterval);
        Assert.Equal(TryOutSpotSubscriptionScopeTypes.Account, subscription.ScopeType);
        Assert.Null(subscription.ScopeId);
    }

    [Fact]
    public async Task ChoosePlanPost_DowngradeToFree_SchedulesStripeCancellationAtPeriodEnd()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync("onboarding-plan-downgrade@example.com", [TryOutSpotRoles.Parent]);
        await AddSubscriptionAsync(factory, user.Id, TryOutSpotPlanCodes.PremiumPlayer, "active");
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, user.Email!);

        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/account/onboarding/choose-plan");
        var response = await client.PostAsync(
            "/account/onboarding/choose-plan",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", antiForgeryToken),
                new("PlanCode", TryOutSpotPlanCodes.FreePlayerParent),
                new("BillingInterval", BillingIntervalCodes.Month)
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/settings", response.Headers.Location?.ToString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscriptions = await dbContext.Subscriptions
            .Where(current => current.UserId == user.Id)
            .ToArrayAsync();

        var paidSubscription = Assert.Single(subscriptions, current => current.PlanType == TryOutSpotPlanCodes.PremiumPlayer);
        Assert.Equal("active", paidSubscription.Status);
        Assert.True(paidSubscription.CancelAtPeriodEnd);
        Assert.NotNull(paidSubscription.CurrentPeriodEnd);

        var freeSubscription = Assert.Single(subscriptions, current => current.PlanType == TryOutSpotPlanCodes.FreePlayerParent);
        Assert.Equal("plan_selected", freeSubscription.Status);
    }

    [Fact]
    public async Task ChoosePlanPost_WithPaidPlanAndStripeConfigured_StartsCheckout()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync("onboarding-plan-checkout@example.com", [TryOutSpotRoles.Parent]);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, user.Email!);

        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/account/onboarding/choose-plan");
        var response = await client.PostAsync(
            "/account/onboarding/choose-plan",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", antiForgeryToken),
                new("PlanCode", TryOutSpotPlanCodes.PremiumPlayer),
                new("BillingInterval", BillingIntervalCodes.Month)
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://checkout.stripe.test/session", response.Headers.Location?.ToString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscription = await dbContext.Subscriptions.SingleAsync(current => current.UserId == user.Id);
        Assert.Equal(TryOutSpotPlanCodes.PremiumPlayer, subscription.PlanType);
        Assert.Equal("checkout_started", subscription.Status);
        Assert.Equal("cus_test_checkout", subscription.StripeCustomerId);
        Assert.Equal("price_premium_month", subscription.StripePriceId);
    }

    [Fact]
    public async Task Onboarding_WithPendingPaidCheckout_KeepsChoosePlanStepOpen()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync("onboarding-plan-pending@example.com", [TryOutSpotRoles.Parent]);
        await AddSubscriptionAsync(factory, user.Id, TryOutSpotPlanCodes.PremiumPlayer, "checkout_started");
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        await LoginWebUserAsync(client, user.Email!);
        var response = await client.GetAsync("/account/onboarding");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("<strong>Choose plan</strong>", html);
        Assert.Contains("href=\"/account/onboarding/choose-plan\">Open</a>", html);
    }

    [Fact]
    public async Task OpenBillingPortal_WithStripeCustomer_RedirectsToStripePortal()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync("settings-portal@example.com", [TryOutSpotRoles.Parent]);
        await AddSubscriptionAsync(factory, user.Id, TryOutSpotPlanCodes.PremiumPlayer, "active");
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, user.Email!);

        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/account/settings");
        var response = await client.PostAsync(
            "/account/settings/open-billing-portal",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", antiForgeryToken)
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("https://billing.stripe.test/session", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task CancelMembershipPost_WithActivePaidMembership_SchedulesStripeCancellationAtPeriodEnd()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync("settings-cancel-paid@example.com", [TryOutSpotRoles.Parent]);
        await AddSubscriptionAsync(factory, user.Id, TryOutSpotPlanCodes.PremiumPlayer, "active");
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, user.Email!);

        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/account/settings");
        var response = await client.PostAsync(
            "/account/settings/cancel-membership",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", antiForgeryToken)
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/settings", response.Headers.Location?.ToString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscription = await dbContext.Subscriptions.SingleAsync(current =>
            current.UserId == user.Id && current.PlanType == TryOutSpotPlanCodes.PremiumPlayer);
        Assert.True(subscription.CancelAtPeriodEnd);
    }

    [Fact]
    public async Task ChoosePlanPost_WithAnnualOnlyPlanAndMonthlyInterval_ShowsValidationError()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync("onboarding-plan-annual-only@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, user.Email!);

        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/account/onboarding/choose-plan");
        var response = await client.PostAsync(
            "/account/onboarding/choose-plan",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", antiForgeryToken),
                new("PlanCode", TryOutSpotPlanCodes.TeamProfessional),
                new("BillingInterval", BillingIntervalCodes.Month)
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("requires annual billing", html, StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscriptions = await dbContext.Subscriptions
            .Where(subscription => subscription.UserId == user.Id)
            .ToArrayAsync();
        Assert.Empty(subscriptions);
    }

    private static void AssertPlayerProfileSections(string html)
    {
        Assert.Contains("<legend>Personal information</legend>", html);
        Assert.Contains("<legend>Contact, location, and visibility</legend>", html);
        Assert.Contains("<legend>School and team</legend>", html);
        Assert.Contains("<legend>Baseball and softball details</legend>", html);
        Assert.Contains("<legend>Sport metrics</legend>", html);
        Assert.Contains("<legend>Media and highlights</legend>", html);
        Assert.Contains("<legend>Social pages</legend>", html);
        Assert.Contains("<legend>Recruiting profiles</legend>", html);
        Assert.Contains("<legend>Sports and positions</legend>", html);
        Assert.Contains("<legend>Profile permissions</legend>", html);
    }

    private static async Task<string> GetAntiForgeryTokenAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);

        return match.Success
            ? match.Groups[1].Value
            : throw new InvalidOperationException($"No anti-forgery token was found on {path}.");
    }

    private static Guid SeedPlayerListingDetail(
        TryOutSpotWebApplicationFactory factory,
        Guid ownerId,
        string contactVisibility)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sport = dbContext.Sports.Single(currentSport => currentSport.Name == "Softball");
        var now = DateTime.UtcNow;
        var playerId = Guid.NewGuid();
        var listingId = Guid.NewGuid();

        dbContext.Players.Add(new Player
        {
            Id = playerId,
            FirstName = "Alex",
            LastName = "Rivera",
            DateOfBirth = new DateTime(2011, 4, 5, 0, 0, 0, DateTimeKind.Utc),
            City = "Wichita",
            State = "KS",
            ZipCode = "67202",
            SchoolName = "Wichita Central High",
            CurrentTeamName = "Aces 16U",
            GraduationYear = 2027,
            Height = "5'9\"",
            Weight = "185 lb",
            ThrowsHand = "Right",
            BatsHand = "Left",
            SixtyYardDash = "6.9 sec",
            HomeToFirstTime = "4.2 sec",
            ExitVelocity = "82 mph",
            ThrowingVelocity = "76 mph",
            PitchVelocity = "72 mph",
            CatcherPopTime = "1.95 sec",
            AdditionalMetrics = "Strong first-step quickness.",
            ContactEmail = "player-contact@example.com",
            ContactPhone = null,
            ContactVisibility = contactVisibility,
            SocialMediaLinks = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["facebook"] = "https://facebook.com/alex.rivera",
                ["highlight_video_1"] = "https://youtube.com/watch?v=alex-highlight"
            }),
            RecruitingProfileLinks = JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["sportsrecruits"] = "https://sportsrecruits.com/athlete/alex-rivera"
            }),
            IsSearchable = true,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        });
        dbContext.PlayerSports.Add(new PlayerSport
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            SportId = sport.Id,
            SkillLevel = "Advanced",
            PrimaryPosition = "Pitcher",
            SecondaryPositions = "Shortstop",
            ExperienceLevel = "Varsity",
            YearsPlaying = 8,
            Availability = "Fall 2026 weekends",
            IsActive = true,
            CreatedAt = now
        });
        dbContext.UserPlayerRelationships.Add(new UserPlayerRelationship
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            PlayerId = playerId,
            Relationship = "Parent",
            CanManage = true,
            CreatedAt = now
        });
        dbContext.PlayerListings.Add(new PlayerListing
        {
            Id = listingId,
            UserId = ownerId,
            PlayerId = playerId,
            SportId = sport.Id,
            ListingType = TryOutSpotPlayerListingTypes.LookingForTeam,
            Title = "Looking for a 2027 fall team",
            Description = "Available for fall 2026 softball opportunities.",
            City = "Wichita",
            State = "KS",
            ZipCode = "67202",
            IsPublished = true,
            IsSearchable = true,
            PublishedAt = now,
            ExpiresAt = now.AddDays(30),
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        });
        dbContext.SaveChanges();
        return listingId;
    }

    private static async Task LoginWebUserAsync(HttpClient client, string email)
    {
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/account/login");
        var loginResponse = await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", antiForgeryToken),
                new("Email", email),
                new("Password", "Tryout2026"),
                new("RememberMe", "true")
            ]));

        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
    }

    private static async Task AddSubscriptionAsync(
        TryOutSpotWebApplicationFactory factory,
        Guid userId,
        string planType,
        string status)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        dbContext.Subscriptions.Add(new TryOutSpot.Web.Data.Entities.Subscription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PlanType = planType,
            Status = status,
            ScopeType = TryOutSpotSubscriptionScopeTypes.Account,
            ScopeId = null,
            StripeCustomerId = $"cus_{Guid.NewGuid():N}",
            StripeSubscriptionId = $"sub_{Guid.NewGuid():N}",
            CurrentPeriodStart = now,
            CurrentPeriodEnd = now.AddMonths(1),
            Amount = 29m,
            Currency = "USD",
            BillingInterval = BillingIntervalCodes.Month,
            CreatedAt = now,
            UpdatedAt = now
        });

        await dbContext.SaveChangesAsync();
    }

    private static WebApplicationFactory<Program> CreateFactoryWithGoogleConfiguration()
    {
        return new TryOutSpotWebApplicationFactory()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Authentication:Google:ClientId"] = "test-google-client-id.apps.googleusercontent.com",
                        ["Authentication:Google:ClientSecret"] = "test-google-client-secret",
                        ["Authentication:Google:CallbackPath"] = "/signin-google"
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.AddAuthentication()
                        .AddGoogle(TryOutSpotSocialLoginProviders.Google, options =>
                        {
                            options.SignInScheme = IdentityConstants.ExternalScheme;
                            options.ClientId = "test-google-client-id.apps.googleusercontent.com";
                            options.ClientSecret = "test-google-client-secret";
                            options.CallbackPath = "/signin-google";
                        });
                    services.PostConfigure<GoogleAuthenticationOptions>(options =>
                    {
                        options.ClientId = "test-google-client-id.apps.googleusercontent.com";
                        options.ClientSecret = "test-google-client-secret";
                        options.CallbackPath = "/signin-google";
                    });
                });
            });
    }

    private static TryOutSpotWebApplicationFactory CreateFactoryWithStripe()
    {
        return new TryOutSpotWebApplicationFactory(services =>
        {
            services.RemoveAll<IStripeBillingService>();
            services.AddSingleton<IStripeBillingService, TestStripeBillingService>();
            services.PostConfigure<StripeBillingOptions>(options =>
            {
                options.SecretKey = "stripe_secret_placeholder";
                options.WebhookSigningSecret = "stripe_webhook_placeholder";
                options.SuccessUrl = "https://example.test/billing/success?session_id={CHECKOUT_SESSION_ID}";
                options.CancelUrl = "https://example.test/billing/cancelled";
                options.PortalReturnUrl = "https://example.test/account/settings";
                options.Plans = new Dictionary<string, StripeBillingPlanPriceOptions>
                {
                    [TryOutSpotPlanCodes.PremiumPlayer] = new()
                    {
                        MonthlyPriceId = "price_premium_month",
                        AnnualPriceId = "price_premium_year"
                    }
                };
            });
        });
    }

    private sealed class TestStripeBillingService : IStripeBillingService
    {
        public Task<StripeCheckoutSessionResult> CreateCheckoutSessionAsync(
            User user,
            string? existingStripeCustomerId,
            BillingPlanDefinition plan,
            string billingInterval,
            string stripePriceId,
            string scopeType,
            Guid? scopeId,
            string? idempotencyKey,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new StripeCheckoutSessionResult(
                "cs_test_checkout",
                "https://checkout.stripe.test/session",
                existingStripeCustomerId ?? "cus_test_checkout"));
        }

        public Task<StripeBillingPortalSessionResult> CreatePortalSessionAsync(
            string stripeCustomerId,
            string? idempotencyKey,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new StripeBillingPortalSessionResult("https://billing.stripe.test/session"));
        }

        public Task<StripeSubscriptionSnapshot?> ScheduleCancellationAtPeriodEndAsync(
            string stripeSubscriptionId,
            string? idempotencyKey,
            CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            return Task.FromResult<StripeSubscriptionSnapshot?>(new StripeSubscriptionSnapshot(
                stripeSubscriptionId,
                "cus_test_checkout",
                null,
                TryOutSpotPlanCodes.PremiumPlayer,
                "price_premium_month",
                "active",
                now,
                now.AddMonths(1),
                null,
                9.99m,
                "usd",
                BillingIntervalCodes.Month,
                TryOutSpotSubscriptionScopeTypes.Account,
                null,
                true,
                null));
        }

        public Task<Stripe.Subscription?> GetSubscriptionAsync(
            string stripeSubscriptionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<Stripe.Subscription?>(null);
        }

        public Event ConstructWebhookEvent(string payload, string signatureHeader)
        {
            throw new NotSupportedException("Webhook construction is not used by these tests.");
        }
    }
}

