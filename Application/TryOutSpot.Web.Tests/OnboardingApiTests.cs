using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Models.Account;
using TryOutSpot.Web.Models.Onboarding;

namespace TryOutSpot.Web.Tests;

public sealed class OnboardingApiTests
{
    [Fact]
    public async Task Options_ReturnsSimpleSignupChoicesAndAllowsSkippingPlanSelection()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/onboarding/options");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await response.Content.ReadFromJsonAsync<OnboardingOptionsResponse>();

        Assert.NotNull(options);
        Assert.True(options.CanSkipPlanSelection);
        Assert.Contains(options.AccountTypes, accountType => accountType.Name == TryOutSpotRoles.Parent);
        Assert.Contains(options.AccountTypes, accountType => accountType.Name == TryOutSpotRoles.Coach);
        Assert.Contains(options.Plans, plan => plan.Code == TryOutSpotPlanCodes.FreePlayerParent);
        Assert.Contains(options.Plans, plan => plan.Code == TryOutSpotPlanCodes.PremiumPlayer);
    }

    [Fact]
    public async Task Options_WithCoachAccountType_ReturnsTeamPlansWithoutForcingPayment()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/onboarding/options?accountTypes=Coach");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var options = await response.Content.ReadFromJsonAsync<OnboardingOptionsResponse>();

        Assert.NotNull(options);
        Assert.True(options.CanSkipPlanSelection);
        Assert.Contains(options.Plans, plan => plan.Code == TryOutSpotPlanCodes.TeamBasic);
        Assert.Contains(options.Plans, plan => plan.Code == TryOutSpotPlanCodes.TeamProfessional);
        Assert.DoesNotContain(options.Plans, plan => plan.Code == TryOutSpotPlanCodes.PremiumPlayer);
    }

    [Fact]
    public async Task Status_ForVerifiedParent_ReturnsRequiredStepsCompleteAndPlayerProfileOptional()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("onboarding-parent@example.com", [TryOutSpotRoles.Parent]);
        var client = factory.CreateClient();
        var tokens = await LoginAsync(client, user.Email!);
        Authorize(client, tokens);

        var response = await client.GetAsync("/api/onboarding/status");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<OnboardingStatusResponse>();

        Assert.NotNull(status);
        Assert.True(status.CanSkipPlanSelection);
        Assert.True(status.IsComplete);
        Assert.Contains(TryOutSpotFeatureCodes.BrowseOpportunities, status.FeatureCodes);

        var accountTypeStep = Assert.Single(status.Steps, step => step.Code == "choose_account_types");
        var emailStep = Assert.Single(status.Steps, step => step.Code == "verify_email");
        var playerStep = Assert.Single(status.Steps, step => step.Code == "add_player_profile");

        Assert.True(accountTypeStep.IsComplete);
        Assert.True(emailStep.IsComplete);
        Assert.False(playerStep.IsRequired);
        Assert.False(playerStep.IsComplete);
    }

    [Fact]
    public async Task UpdateAccountTypes_ReplacesPublicRolesAndRefreshesRecommendedPlans()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("onboarding-update@example.com", [TryOutSpotRoles.Parent]);
        var client = factory.CreateClient();
        var tokens = await LoginAsync(client, user.Email!);
        Authorize(client, tokens);

        var response = await client.PostAsJsonAsync(
            "/api/onboarding/account-types",
            new UpdateOnboardingAccountTypesRequest
            {
                AccountTypes = [TryOutSpotRoles.Coach, TryOutSpotRoles.TeamManager]
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<OnboardingStatusResponse>();

        Assert.NotNull(status);
        Assert.Contains(TryOutSpotRoles.Coach, status.User.AccountTypes);
        Assert.Contains(TryOutSpotRoles.TeamManager, status.User.AccountTypes);
        Assert.DoesNotContain(TryOutSpotRoles.Parent, status.User.AccountTypes);
        Assert.Contains(status.RecommendedPlans, plan => plan.Code == TryOutSpotPlanCodes.TeamBasic);
        Assert.Contains(status.Steps, step => step.Code == "choose_plan" && !step.IsRequired);
        Assert.DoesNotContain(status.Steps, step => step.Code == "add_player_profile");
    }

    [Fact]
    public async Task UpdateAccountTypes_WithUnsupportedRole_ReturnsBadRequest()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("onboarding-invalid@example.com", [TryOutSpotRoles.Parent]);
        var client = factory.CreateClient();
        var tokens = await LoginAsync(client, user.Email!);
        Authorize(client, tokens);

        var response = await client.PostAsJsonAsync(
            "/api/onboarding/account-types",
            new UpdateOnboardingAccountTypesRequest
            {
                AccountTypes = [TryOutSpotRoles.PlatformAdmin]
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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
