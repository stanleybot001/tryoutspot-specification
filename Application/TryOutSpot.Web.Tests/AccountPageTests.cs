using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;

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
        Assert.Contains("name=\"DateOfBirth\"", html);
        Assert.Contains("name=\"ZipCode\"", html);
        Assert.Contains("required-marker", html);
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
}
