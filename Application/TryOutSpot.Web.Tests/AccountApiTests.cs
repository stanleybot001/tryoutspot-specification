using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Models.Account;

namespace TryOutSpot.Web.Tests;

public sealed class AccountApiTests
{
    [Fact]
    public async Task Register_WithMultipleAccountTypes_CreatesActiveUserAndRoles()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();
        var request = NewRegisterRequest("multi-role@example.com", ["Parent", "TeamRepresentative"]);

        var response = await client.PostAsJsonAsync("/api/account/register", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var account = await response.Content.ReadFromJsonAsync<UserAccountResponse>();
        Assert.NotNull(account);
        Assert.True(account.IsActive);
        Assert.Equal(["Parent", "TeamRepresentative"], account.AccountTypes.OrderBy(role => role));

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await userManager.FindByEmailAsync(request.Email);

        Assert.NotNull(user);
        Assert.True(user.IsActive);
        Assert.True(await userManager.IsInRoleAsync(user, TryOutSpotRoles.Parent));
        Assert.True(await userManager.IsInRoleAsync(user, TryOutSpotRoles.TeamRepresentative));
    }

    [Theory]
    [InlineData("PlatformAdmin")]
    [InlineData("UnsupportedRole")]
    public async Task Register_WithUnsupportedAccountType_ReturnsBadRequest(string accountType)
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();
        var request = NewRegisterRequest($"bad-role-{accountType}@example.com", [accountType]);

        var response = await client.PostAsJsonAsync("/api/account/register", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_ReturnsBadRequest()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();
        var request = NewRegisterRequest("duplicate@example.com", ["Parent"]);

        var firstResponse = await client.PostAsJsonAsync("/api/account/register", request);
        var duplicateResponse = await client.PostAsJsonAsync("/api/account/register", request);

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
    }

    [Fact]
    public async Task Register_WithWeakPassword_ReturnsBadRequest()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();
        var request = NewRegisterRequest("weak-password@example.com", ["Parent"]);
        request.Password = "weak";

        var response = await client.PostAsJsonAsync("/api/account/register", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_ReturnsGenericMessage_ForExistingAndMissingAccounts()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "forgot@example.com", ["Parent"]);

        var existingResponse = await client.PostAsJsonAsync(
            "/api/account/forgot-password",
            new ForgotPasswordRequest { Email = "forgot@example.com" });
        var missingResponse = await client.PostAsJsonAsync(
            "/api/account/forgot-password",
            new ForgotPasswordRequest { Email = "missing@example.com" });

        Assert.Equal(HttpStatusCode.OK, existingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, missingResponse.StatusCode);

        var existingBody = await existingResponse.Content.ReadFromJsonAsync<AccountActionResponse>();
        var missingBody = await missingResponse.Content.ReadFromJsonAsync<AccountActionResponse>();
        Assert.Equal(existingBody, missingBody);

        var emailSender = factory.Services.GetRequiredService<TestAccountEmailSender>();
        Assert.True(emailSender.TryGetPasswordResetToken("forgot@example.com", out _));
        Assert.False(emailSender.TryGetPasswordResetToken("missing@example.com", out _));
    }

    [Fact]
    public async Task ResetPassword_WithValidToken_ChangesPassword()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "reset-valid@example.com", ["Parent"]);
        await client.PostAsJsonAsync(
            "/api/account/forgot-password",
            new ForgotPasswordRequest { Email = "reset-valid@example.com" });

        var emailSender = factory.Services.GetRequiredService<TestAccountEmailSender>();
        Assert.True(emailSender.TryGetPasswordResetToken("reset-valid@example.com", out var token));

        var response = await client.PostAsJsonAsync(
            "/api/account/reset-password",
            new ResetPasswordRequest
            {
                Email = "reset-valid@example.com",
                Token = token,
                NewPassword = "NewTryout2026"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var user = await userManager.FindByEmailAsync("reset-valid@example.com");

        Assert.NotNull(user);
        Assert.True(await userManager.CheckPasswordAsync(user, "NewTryout2026"));
    }

    [Fact]
    public async Task ResetPassword_WithInvalidToken_ReturnsBadRequest()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "reset-invalid@example.com", ["Parent"]);

        var response = await client.PostAsJsonAsync(
            "/api/account/reset-password",
            new ResetPasswordRequest
            {
                Email = "reset-invalid@example.com",
                Token = "invalid-token",
                NewPassword = "NewTryout2026"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteAccount_WithValidPassword_SoftDeletesAndLocksAccount()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "delete-valid@example.com", ["Parent"]);

        var response = await client.PostAsJsonAsync(
            "/api/account/delete-account",
            new DeleteAccountRequest
            {
                Email = "delete-valid@example.com",
                Password = "Tryout2026",
                Reason = "test cleanup"
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = dbContext.Users.Single(currentUser => currentUser.Email == "delete-valid@example.com");

        Assert.False(user.IsActive);
        Assert.True(user.LockoutEnabled);
        Assert.NotNull(user.LockoutEnd);
        Assert.True(user.LockoutEnd > DateTimeOffset.UtcNow.AddYears(50));
    }

    [Fact]
    public async Task DeleteAccount_WithWrongPassword_ReturnsBadRequestAndLeavesAccountActive()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "delete-wrong-password@example.com", ["Parent"]);

        var response = await client.PostAsJsonAsync(
            "/api/account/delete-account",
            new DeleteAccountRequest
            {
                Email = "delete-wrong-password@example.com",
                Password = "WrongTryout2026"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = dbContext.Users.Single(currentUser => currentUser.Email == "delete-wrong-password@example.com");

        Assert.True(user.IsActive);
        Assert.Null(user.LockoutEnd);
    }

    [Fact]
    public async Task ResetPassword_ForSoftDeletedAccount_ReturnsBadRequest()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "deleted-reset@example.com", ["Parent"]);
        await client.PostAsJsonAsync(
            "/api/account/forgot-password",
            new ForgotPasswordRequest { Email = "deleted-reset@example.com" });
        var emailSender = factory.Services.GetRequiredService<TestAccountEmailSender>();
        Assert.True(emailSender.TryGetPasswordResetToken("deleted-reset@example.com", out var token));
        await client.PostAsJsonAsync(
            "/api/account/delete-account",
            new DeleteAccountRequest
            {
                Email = "deleted-reset@example.com",
                Password = "Tryout2026"
            });

        var response = await client.PostAsJsonAsync(
            "/api/account/reset-password",
            new ResetPasswordRequest
            {
                Email = "deleted-reset@example.com",
                Token = token,
                NewPassword = "NewTryout2026"
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static RegisterUserRequest NewRegisterRequest(string email, IReadOnlyCollection<string> accountTypes)
    {
        return new RegisterUserRequest
        {
            Email = email,
            Password = "Tryout2026",
            FirstName = "Taylor",
            LastName = "Morgan",
            PhoneNumber = "555-555-1234",
            ZipCode = "73102",
            City = "Oklahoma City",
            State = "OK",
            AccountTypes = accountTypes
        };
    }

    private static async Task RegisterAsync(
        HttpClient client,
        string email,
        IReadOnlyCollection<string> accountTypes)
    {
        var response = await client.PostAsJsonAsync("/api/account/register", NewRegisterRequest(email, accountTypes));
        response.EnsureSuccessStatusCode();
    }
}

