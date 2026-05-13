using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Security;

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
}
