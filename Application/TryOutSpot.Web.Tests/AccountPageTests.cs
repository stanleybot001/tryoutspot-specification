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
                new("AccountTypes", TryOutSpotRoles.Coach),
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
        Assert.Contains(TryOutSpotRoles.Coach, roles);
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
        Assert.Contains("name=\"accountTypes\"", html);
        Assert.Contains("Recommended plan options", html);
        Assert.Contains("href=\"/account/onboarding/add-player-profile\"", html);
        Assert.Contains("href=\"/account/onboarding/choose-plan\"", html);
    }

    [Fact]
    public async Task Onboarding_WithCoachRole_ShowsTeamOrOrganizationStepLink()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        await factory.CreateUserAsync("web-onboarding-coach@example.com", [TryOutSpotRoles.Coach]);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        await LoginWebUserAsync(client, "web-onboarding-coach@example.com");
        var response = await client.GetAsync("/account/onboarding");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Add team or organization", html);
        Assert.Contains("href=\"/account/onboarding/add-team-or-organization\"", html);
    }

    [Fact]
    public async Task Settings_RequiresWebCookieAndRendersAccountManagementForms()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        await factory.CreateUserAsync("settings-page@example.com", [TryOutSpotRoles.Parent, TryOutSpotRoles.Coach]);
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
        Assert.Contains("name=\"accountTypes\"", html);
        Assert.Contains("name=\"SmsConsent.SmsConsentAccepted\"", html);
        Assert.Contains("Membership access", html);
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
                new("accountTypes", TryOutSpotRoles.Coach)
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
        Assert.Contains(TryOutSpotRoles.Coach, roles);
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
                new("SelectedSportIds", sportId.ToString())
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/onboarding", response.Headers.Location?.ToString());

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
        Assert.Equal(user.Id, relationship.UserId);
        Assert.Equal(player.Id, relationship.PlayerId);
        Assert.Equal("Parent", relationship.Relationship);
        Assert.Equal(player.Id, playerSport.PlayerId);
        Assert.Equal(sportId, playerSport.SportId);

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
    public async Task AddTeamOrOrganizationPage_AndPost_CreateLinkedTeamAndOrganizationRecords()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("onboarding-team-setup@example.com", [TryOutSpotRoles.Coach]);

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
                new("TeamRole", TryOutSpotRoles.Coach),
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
        Assert.Equal(TryOutSpotRoles.Coach, userTeamRole.Role);
        Assert.Equal(team.Id, teamSport.TeamId);
        Assert.Equal(sportId, teamSport.SportId);

        var teamSocialLinks = JsonSerializer.Deserialize<Dictionary<string, string>>(team.SocialMediaLinks ?? "{}");
        Assert.Equal("https://facebook.com/midamserv", teamSocialLinks?["facebook"]);
        Assert.Equal("https://x.com/midamserv", teamSocialLinks?["x"]);
        Assert.Equal("https://instagram.com/midamserv", teamSocialLinks?["instagram"]);
        Assert.Equal("https://youtube.com/@midamserv", teamSocialLinks?["youtube"]);
        Assert.Equal("https://tiktok.com/midamserv", teamSocialLinks?["tiktok"]);
        Assert.Equal("https://www.youtube.com/watch?v=thunder123", teamSocialLinks?["highlight_video_1"]);
        Assert.Equal("https://www.hudl.com/video/thunder456", teamSocialLinks?["highlight_video_2"]);

        var organizationSocialLinks = JsonSerializer.Deserialize<Dictionary<string, string>>(organization.SocialMediaLinks ?? "{}");
        Assert.Equal("https://facebook.com/midamserv", organizationSocialLinks?["facebook"]);
        Assert.Equal("https://x.com/midamserv", organizationSocialLinks?["x"]);
        Assert.Equal("https://instagram.com/midamserv", organizationSocialLinks?["instagram"]);
        Assert.Equal("https://youtube.com/@midamserv", organizationSocialLinks?["youtube"]);
        Assert.Equal("https://tiktok.com/midamserv", organizationSocialLinks?["tiktok"]);
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
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new StripeCheckoutSessionResult(
                "cs_test_checkout",
                "https://checkout.stripe.test/session",
                existingStripeCustomerId ?? "cus_test_checkout"));
        }

        public Task<StripeBillingPortalSessionResult> CreatePortalSessionAsync(
            string stripeCustomerId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new StripeBillingPortalSessionResult("https://billing.stripe.test/session"));
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
