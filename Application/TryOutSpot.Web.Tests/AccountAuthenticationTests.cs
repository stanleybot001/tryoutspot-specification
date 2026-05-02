using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Models.Account;

namespace TryOutSpot.Web.Tests;

public sealed class AccountAuthenticationTests
{
    [Fact]
    public async Task Register_SendsEmailConfirmation_AndLoginRequiresVerification()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();
        var email = "verify-login@example.com";
        await factory.RegisterUserAsync(email, ["Parent"]);

        var emailSender = factory.Services.GetRequiredService<TestAccountEmailSender>();
        Assert.True(emailSender.TryGetEmailConfirmationToken(email, out var token));

        var blockedLogin = await client.PostAsJsonAsync(
            "/api/account/login",
            new LoginRequest
            {
                Email = email,
                Password = "Tryout2026"
            });
        Assert.Equal(HttpStatusCode.BadRequest, blockedLogin.StatusCode);

        var verifyResponse = await client.PostAsJsonAsync(
            "/api/account/verify-email",
            new VerifyEmailRequest
            {
                Email = email,
                Token = token
            });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        var loginResponse = await client.PostAsJsonAsync(
            "/api/account/login",
            new LoginRequest
            {
                Email = email,
                Password = "Tryout2026"
            });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var tokenResponse = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(tokenResponse);
        Assert.Equal("Bearer", tokenResponse.TokenType);
        Assert.False(string.IsNullOrWhiteSpace(tokenResponse.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(tokenResponse.RefreshToken));
    }

    [Fact]
    public async Task ResendEmailVerification_ReturnsGenericResponse_AndSendsForActiveUnverifiedAccount()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();
        var email = "resend@example.com";
        await factory.RegisterUserAsync(email, ["Parent"]);

        var existingResponse = await client.PostAsJsonAsync(
            "/api/account/resend-email-verification",
            new ResendEmailVerificationRequest { Email = email });
        var missingResponse = await client.PostAsJsonAsync(
            "/api/account/resend-email-verification",
            new ResendEmailVerificationRequest { Email = "missing-resend@example.com" });

        Assert.Equal(HttpStatusCode.OK, existingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, missingResponse.StatusCode);

        var existingBody = await existingResponse.Content.ReadFromJsonAsync<AccountActionResponse>();
        var missingBody = await missingResponse.Content.ReadFromJsonAsync<AccountActionResponse>();
        Assert.Equal(existingBody, missingBody);

        var emailSender = factory.Services.GetRequiredService<TestAccountEmailSender>();
        Assert.True(emailSender.TryGetEmailConfirmationToken(email, out _));
        Assert.False(emailSender.TryGetEmailConfirmationToken("missing-resend@example.com", out _));
    }

    [Fact]
    public async Task AuthenticatedMe_ReturnsCurrentVerifiedUser()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();
        var email = "me@example.com";
        await factory.RegisterUserAsync(email, ["Parent", "Coach"]);
        await factory.ConfirmEmailAsync(email);

        var tokenResponse = await LoginAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            tokenResponse.TokenType,
            tokenResponse.AccessToken);

        var response = await client.GetAsync("/api/account/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var currentUser = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(currentUser);
        Assert.Equal(email, currentUser.Email);
        Assert.True(currentUser.EmailConfirmed);
        Assert.Contains("Parent", currentUser.AccountTypes);
        Assert.Contains("Coach", currentUser.AccountTypes);
    }

    [Fact]
    public async Task RefreshToken_RotatesRefreshTokenAndRejectsOldToken()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();
        var email = "refresh@example.com";
        await factory.RegisterUserAsync(email, ["Parent"]);
        await factory.ConfirmEmailAsync(email);
        var loginTokens = await LoginAsync(client, email);

        var refreshResponse = await client.PostAsJsonAsync(
            "/api/account/refresh-token",
            new TokenRefreshRequest { RefreshToken = loginTokens.RefreshToken });

        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshedTokens = await refreshResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(refreshedTokens);
        Assert.NotEqual(loginTokens.RefreshToken, refreshedTokens.RefreshToken);

        var oldRefreshResponse = await client.PostAsJsonAsync(
            "/api/account/refresh-token",
            new TokenRefreshRequest { RefreshToken = loginTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, oldRefreshResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesRefreshToken()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();
        var email = "logout@example.com";
        await factory.RegisterUserAsync(email, ["Parent"]);
        await factory.ConfirmEmailAsync(email);
        var loginTokens = await LoginAsync(client, email);

        var logoutResponse = await client.PostAsJsonAsync(
            "/api/account/logout",
            new LogoutRequest { RefreshToken = loginTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);

        var refreshResponse = await client.PostAsJsonAsync(
            "/api/account/refresh-token",
            new TokenRefreshRequest { RefreshToken = loginTokens.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    [Fact]
    public async Task SoftDeletedAccount_InvalidatesExistingBearerToken()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();
        var email = "deleted-bearer@example.com";
        await factory.RegisterUserAsync(email, ["Parent"]);
        await factory.ConfirmEmailAsync(email);
        var loginTokens = await LoginAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            loginTokens.TokenType,
            loginTokens.AccessToken);

        var deleteResponse = await client.PostAsJsonAsync(
            "/api/account/delete-account",
            new DeleteAccountRequest
            {
                Email = email,
                Password = "Tryout2026"
            });
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var meResponse = await client.GetAsync("/api/account/me");

        Assert.Equal(HttpStatusCode.Unauthorized, meResponse.StatusCode);
    }

    [Fact]
    public async Task SendAndVerifyPhoneVerification_ConfirmsPhoneNumber()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();
        var email = "phone@example.com";
        var phoneNumber = "555-555-4321";
        await factory.RegisterUserAsync(email, ["Parent"]);
        await factory.ConfirmEmailAsync(email);
        var loginTokens = await LoginAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            loginTokens.TokenType,
            loginTokens.AccessToken);

        var sendResponse = await client.PostAsJsonAsync(
            "/api/account/send-phone-verification",
            new SendPhoneVerificationRequest { PhoneNumber = phoneNumber });
        Assert.Equal(HttpStatusCode.OK, sendResponse.StatusCode);

        var smsSender = factory.Services.GetRequiredService<TestAccountSmsSender>();
        Assert.True(smsSender.TryGetCode(phoneNumber, out var code));

        var verifyResponse = await client.PostAsJsonAsync(
            "/api/account/verify-phone",
            new VerifyPhoneRequest
            {
                PhoneNumber = phoneNumber,
                Code = code
            });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await userManager.FindByEmailAsync(email);

        Assert.NotNull(user);
        Assert.Equal(phoneNumber, user.PhoneNumber);
        Assert.True(user.PhoneNumberConfirmed);
    }

    [Fact]
    public async Task RepeatedFailedLogins_LockAccount()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();
        var email = "lockout@example.com";
        await factory.RegisterUserAsync(email, ["Parent"]);
        await factory.ConfirmEmailAsync(email);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await client.PostAsJsonAsync(
                "/api/account/login",
                new LoginRequest
                {
                    Email = email,
                    Password = "WrongTryout2026"
                });
        }

        var lockedResponse = await client.PostAsJsonAsync(
            "/api/account/login",
            new LoginRequest
            {
                Email = email,
                Password = "Tryout2026"
            });

        Assert.Equal((HttpStatusCode)423, lockedResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await userManager.FindByEmailAsync(email);
        Assert.NotNull(user);
        Assert.NotNull(user.LockoutEnd);
    }

    [Fact]
    public async Task AccountEndpoints_AreRateLimited()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();

        HttpResponseMessage response = null!;
        for (var attempt = 0; attempt < 21; attempt++)
        {
            response = await client.PostAsJsonAsync(
                "/api/account/forgot-password",
                new ForgotPasswordRequest { Email = "rate-limit@example.com" });
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
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
            ?? throw new InvalidOperationException("Login did not return a token response.");
    }
}
