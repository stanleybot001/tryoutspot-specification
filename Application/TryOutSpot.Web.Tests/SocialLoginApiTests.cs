using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Models.Account;
using TryOutSpot.Web.Models.SocialLogin;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Tests;

public sealed class SocialLoginApiTests
{
    [Fact]
    public async Task Providers_ReturnsSupportedProvidersAsUnconfiguredByDefault()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/social-login/providers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var providers = await response.Content.ReadFromJsonAsync<SocialLoginProviderResponse[]>();

        Assert.NotNull(providers);
        Assert.Contains(providers, provider => provider.Provider == TryOutSpotSocialLoginProviders.Google);
        Assert.Contains(providers, provider => provider.Provider == TryOutSpotSocialLoginProviders.Facebook);
        Assert.Contains(providers, provider => provider.Provider == TryOutSpotSocialLoginProviders.Apple);
        Assert.All(providers, provider => Assert.False(provider.IsConfigured));
    }

    [Fact]
    public async Task Register_WithExternalLoginToken_CreatesExternalOnlyUserAndReturnsTokens()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();
        var externalToken = CreateExternalToken(
            factory,
            TryOutSpotSocialLoginProviders.Google,
            "google-register-123",
            "social-register@example.com");

        var response = await client.PostAsJsonAsync(
            "/api/social-login/register",
            new SocialLoginRegisterRequest
            {
                ExternalLoginToken = externalToken,
                AccountTypes = ["Parent", "Coach"]
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var tokens = await response.Content.ReadFromJsonAsync<AuthTokenResponse>();

        Assert.NotNull(tokens);
        Assert.Equal("social-register@example.com", tokens.User.Email);
        Assert.Contains(TryOutSpotRoles.Parent, tokens.User.AccountTypes);
        Assert.Contains(TryOutSpotRoles.Coach, tokens.User.AccountTypes);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await userManager.FindByLoginAsync(TryOutSpotSocialLoginProviders.Google, "google-register-123");

        Assert.NotNull(user);
        Assert.True(user.EmailConfirmed);
        Assert.Null(user.PasswordHash);
    }

    [Fact]
    public async Task Register_WithExistingEmail_ReturnsBadRequest()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();
        await factory.CreateUserAsync("existing-social@example.com", [TryOutSpotRoles.Parent]);
        var externalToken = CreateExternalToken(
            factory,
            TryOutSpotSocialLoginProviders.Google,
            "google-existing-123",
            "existing-social@example.com");

        var response = await client.PostAsJsonAsync(
            "/api/social-login/register",
            new SocialLoginRegisterRequest
            {
                ExternalLoginToken = externalToken,
                AccountTypes = ["Parent"]
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Link_WithExternalLoginToken_AddsLoginToAuthenticatedAccount()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var email = "link-social@example.com";
        await factory.CreateUserAsync(email, [TryOutSpotRoles.Parent]);
        var client = factory.CreateClient();
        var tokens = await LoginAsync(client, email);
        Authorize(client, tokens);
        var externalToken = CreateExternalToken(
            factory,
            TryOutSpotSocialLoginProviders.Facebook,
            "facebook-link-123",
            email);

        var response = await client.PostAsJsonAsync(
            "/api/social-login/link",
            new SocialLoginLinkRequest { ExternalLoginToken = externalToken });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var linkedResponse = await client.GetAsync("/api/social-login/linked");
        Assert.Equal(HttpStatusCode.OK, linkedResponse.StatusCode);
        var linkedLogins = await linkedResponse.Content.ReadFromJsonAsync<LinkedSocialLoginResponse[]>();
        Assert.NotNull(linkedLogins);
        Assert.Contains(linkedLogins, login => login.Provider == TryOutSpotSocialLoginProviders.Facebook);
    }

    [Fact]
    public async Task Unlink_PreventsRemovingOnlySignInMethod()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();
        var externalToken = CreateExternalToken(
            factory,
            TryOutSpotSocialLoginProviders.Google,
            "google-unlink-123",
            "unlink-social@example.com");

        var registerResponse = await client.PostAsJsonAsync(
            "/api/social-login/register",
            new SocialLoginRegisterRequest
            {
                ExternalLoginToken = externalToken,
                AccountTypes = ["Parent"]
            });
        registerResponse.EnsureSuccessStatusCode();
        var tokens = await registerResponse.Content.ReadFromJsonAsync<AuthTokenResponse>()
            ?? throw new InvalidOperationException("Social registration did not return tokens.");
        Authorize(client, tokens);

        var unlinkResponse = await client.PostAsJsonAsync(
            "/api/social-login/unlink",
            new SocialLoginUnlinkRequest { Provider = TryOutSpotSocialLoginProviders.Google });

        Assert.Equal(HttpStatusCode.BadRequest, unlinkResponse.StatusCode);
    }

    private static string CreateExternalToken(
        TryOutSpotWebApplicationFactory factory,
        string provider,
        string providerKey,
        string email)
    {
        var ticketService = factory.Services.GetRequiredService<IExternalLoginTicketService>();
        return ticketService.Create(new ExternalLoginTicket(
            provider,
            providerKey,
            email,
            EmailVerified: true,
            FirstName: "Social",
            LastName: "User"));
    }

    private static async Task<AuthTokenResponse> LoginAsync(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync(
            "/api/account/login",
            new LoginRequest
            {
                Email = email,
                Password = "Tryout2026"
            });

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AuthTokenResponse>()
            ?? throw new InvalidOperationException("Login did not return token response.");
    }

    private static void Authorize(HttpClient client, AuthTokenResponse tokenResponse)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            tokenResponse.TokenType,
            tokenResponse.AccessToken);
    }
}
