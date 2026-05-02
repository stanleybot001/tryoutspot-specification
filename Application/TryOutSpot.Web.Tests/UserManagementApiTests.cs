using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Models.Account;
using TryOutSpot.Web.Models.UserManagement;

namespace TryOutSpot.Web.Tests;

public sealed class UserManagementApiTests
{
    [Fact]
    public async Task ListUsers_RequiresAuthentication()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/user-management/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ListUsers_RequiresPlatformAdminRole()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var email = "not-admin@example.com";
        await factory.CreateUserAsync(email, [TryOutSpotRoles.Parent]);
        var client = factory.CreateClient();
        var tokens = await LoginAsync(client, email);
        Authorize(client, tokens);

        var response = await client.GetAsync("/api/user-management/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateUser_WithMultipleAccountTypes_CreatesManagedUser()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = await CreateAdminClientAsync(factory);

        var response = await client.PostAsJsonAsync(
            "/api/user-management/users",
            new CreateManagedUserRequest
            {
                Email = "managed-create@example.com",
                Password = "Tryout2026",
                FirstName = "Jordan",
                LastName = "Coach",
                AccountTypes = ["Parent", "Coach"],
                EmailConfirmed = true
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var user = await response.Content.ReadFromJsonAsync<ManagedUserDetailResponse>();

        Assert.NotNull(user);
        Assert.Equal("managed-create@example.com", user.Email);
        Assert.Contains(TryOutSpotRoles.Parent, user.AccountTypes);
        Assert.Contains(TryOutSpotRoles.Coach, user.AccountTypes);
        Assert.True(user.IsActive);
        Assert.True(user.EmailConfirmed);
    }

    [Fact]
    public async Task ListUsers_FiltersBySearchAndAccountType()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = await CreateAdminClientAsync(factory);
        await factory.CreateUserAsync("filter-coach@example.com", [TryOutSpotRoles.Coach]);
        await factory.CreateUserAsync("filter-parent@example.com", [TryOutSpotRoles.Parent]);

        var response = await client.GetAsync(
            "/api/user-management/users?search=filter&accountType=Coach&page=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var list = await response.Content.ReadFromJsonAsync<ManagedUserListResponse>();

        Assert.NotNull(list);
        Assert.Equal(1, list.TotalCount);
        var user = Assert.Single(list.Users);
        Assert.Equal("filter-coach@example.com", user.Email);
        Assert.Contains(TryOutSpotRoles.Coach, user.AccountTypes);
    }

    [Fact]
    public async Task UpdateAccountTypes_ReplacesRolesAndAllowsCombinations()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = await CreateAdminClientAsync(factory);
        var user = await factory.CreateUserAsync("role-update@example.com", [TryOutSpotRoles.Parent]);

        var response = await client.PostAsJsonAsync(
            $"/api/user-management/users/{user.Id}/account-types",
            new UpdateManagedUserAccountTypesRequest
            {
                AccountTypes = ["Coach", "TeamManager"]
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<ManagedUserDetailResponse>();

        Assert.NotNull(updated);
        Assert.DoesNotContain(TryOutSpotRoles.Parent, updated.AccountTypes);
        Assert.Contains(TryOutSpotRoles.Coach, updated.AccountTypes);
        Assert.Contains(TryOutSpotRoles.TeamManager, updated.AccountTypes);
    }

    [Fact]
    public async Task DeactivateAndReactivateUser_UpdatesSoftDeleteState()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = await CreateAdminClientAsync(factory);
        var user = await factory.CreateUserAsync("deactivate-managed@example.com", [TryOutSpotRoles.Parent]);

        var deactivateResponse = await client.PostAsJsonAsync(
            $"/api/user-management/users/{user.Id}/deactivate",
            new ManagedUserStateChangeRequest { Reason = "test" });

        Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var deactivated = await dbContext.Users.SingleAsync(currentUser => currentUser.Id == user.Id);
            Assert.False(deactivated.IsActive);
            Assert.NotNull(deactivated.LockoutEnd);
        }

        var reactivateResponse = await client.PostAsJsonAsync(
            $"/api/user-management/users/{user.Id}/reactivate",
            new ManagedUserStateChangeRequest { Reason = "test" });

        Assert.Equal(HttpStatusCode.OK, reactivateResponse.StatusCode);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reactivated = await dbContext.Users.SingleAsync(currentUser => currentUser.Id == user.Id);
            Assert.True(reactivated.IsActive);
            Assert.Null(reactivated.LockoutEnd);
        }
    }

    [Fact]
    public async Task PlatformAdmin_CannotRemoveOwnPlatformAdminRole()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var adminTokens = await factory.LoginAsPlatformAdminAsync();
        var client = factory.CreateClient();
        Authorize(client, adminTokens);

        var response = await client.PostAsJsonAsync(
            $"/api/user-management/users/{adminTokens.User.UserId}/account-types",
            new UpdateManagedUserAccountTypesRequest
            {
                AccountTypes = [TryOutSpotRoles.Parent]
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<HttpClient> CreateAdminClientAsync(TryOutSpotWebApplicationFactory factory)
    {
        var adminTokens = await factory.LoginAsPlatformAdminAsync();
        var client = factory.CreateClient();
        Authorize(client, adminTokens);
        return client;
    }

    private static void Authorize(HttpClient client, AuthTokenResponse tokenResponse)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            tokenResponse.TokenType,
            tokenResponse.AccessToken);
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
}
