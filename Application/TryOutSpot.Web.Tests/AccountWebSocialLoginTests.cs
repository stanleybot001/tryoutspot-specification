using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Tests;

public sealed class AccountWebSocialLoginTests
{
    [Fact]
    public async Task CompleteSocialRegistration_WithVerifiedEmail_CreatesAccountAndSignsIn()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var email = $"web-social-signup-{Guid.NewGuid():N}@example.com";
        var externalLoginToken = CreateExternalToken(
            factory,
            TryOutSpotSocialLoginProviders.Google,
            "google-web-signup-123",
            email,
            emailVerified: true);
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var page = await client.GetAsync($"/account/complete-social-registration?externalLoginToken={Uri.EscapeDataString(externalLoginToken)}");
        page.EnsureSuccessStatusCode();
        var antiForgeryToken = await ReadAntiForgeryTokenAsync(page);

        var response = await client.PostAsync(
            "/account/complete-social-registration",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = antiForgeryToken,
                ["ExternalLoginToken"] = externalLoginToken,
                ["FirstName"] = "Social",
                ["LastName"] = "Signup",
                ["DateOfBirth"] = "1973-02-22",
                ["AccountTypes"] = TryOutSpotRoles.Parent
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/onboarding", response.Headers.Location?.OriginalString);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await userManager.FindByLoginAsync(TryOutSpotSocialLoginProviders.Google, "google-web-signup-123");

        Assert.NotNull(user);
        Assert.Equal(email, user.Email);
        Assert.True(user.EmailConfirmed);
        Assert.Null(user.PasswordHash);
        Assert.Equal(new DateTime(1973, 2, 22, 0, 0, 0, DateTimeKind.Utc), user.DateOfBirth);

        var roles = await userManager.GetRolesAsync(user);
        Assert.Contains(TryOutSpotRoles.Parent, roles);
    }

    [Fact]
    public async Task LinkSocialLogin_WithVerifiedExistingEmail_LinksAndSignsInWithoutPassword()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var email = "web-social-link@example.com";
        await factory.CreateUserAsync(email, [TryOutSpotRoles.Parent], emailConfirmed: false);
        var externalLoginToken = CreateExternalToken(
            factory,
            TryOutSpotSocialLoginProviders.Google,
            "google-web-link-123",
            email,
            emailVerified: true);
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var page = await client.GetAsync($"/account/complete-social-registration?externalLoginToken={Uri.EscapeDataString(externalLoginToken)}");
        page.EnsureSuccessStatusCode();
        var antiForgeryToken = await ReadAntiForgeryTokenAsync(page);

        var response = await client.PostAsync(
            "/account/link-social-login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = antiForgeryToken,
                ["externalLoginToken"] = externalLoginToken
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/account/onboarding", response.Headers.Location?.OriginalString);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await userManager.FindByLoginAsync(TryOutSpotSocialLoginProviders.Google, "google-web-link-123");

        Assert.NotNull(user);
        Assert.Equal(email, user.Email);
        Assert.True(user.EmailConfirmed);
    }

    private static string CreateExternalToken(
        TryOutSpotWebApplicationFactory factory,
        string provider,
        string providerKey,
        string email,
        bool emailVerified)
    {
        var ticketService = factory.Services.GetRequiredService<IExternalLoginTicketService>();
        return ticketService.Create(new ExternalLoginTicket(
            provider,
            providerKey,
            email,
            emailVerified,
            FirstName: "Social",
            LastName: "User",
            ProfileImageUrl: null));
    }

    private static async Task<string> ReadAntiForgeryTokenAsync(HttpResponseMessage response)
    {
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"",
            RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            throw new InvalidOperationException("The antiforgery token was not found in the response.");
        }

        return WebUtility.HtmlDecode(match.Groups["token"].Value);
    }
}
