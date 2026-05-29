using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Models.Account;
using TryOutSpot.Web.Models.Dashboard;

namespace TryOutSpot.Web.Tests;

public sealed class ActivationAssistanceApiTests
{
    [Fact]
    public async Task ActivationAssistance_WithIncompleteTeamAndNoListings_ReturnsPrompt()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("activation-api-team@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var teamId = await SeedManagedTeamAsync(factory, user.Id, completeProfile: false);
        await BackdateUserAsync(factory, user.Id);
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        var response = await client.GetAsync("/api/dashboard/activation-assistance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var prompt = await response.Content.ReadFromJsonAsync<ActivationAssistancePromptResponse>();
        Assert.NotNull(prompt);
        Assert.True(prompt.ShouldShow);
        Assert.Equal(ActivationAssistancePromptKeys.TeamFirstListing, prompt.PromptKey);
        Assert.Equal(teamId, prompt.TeamId);
        Assert.Equal("Need help getting Activation Assist Aces ready?", prompt.Title);
        Assert.Contains(prompt.Reasons, reason => reason.Code == "team_level_missing");
        Assert.Contains(prompt.Reasons, reason => reason.Code == "team_sports_missing");
        Assert.Contains(prompt.Reasons, reason => reason.Code == "team_location_missing");
        Assert.Contains(prompt.Reasons, reason => reason.Code == "team_contact_missing");
        Assert.Equal("/account/onboarding/team-opportunities/" + teamId + "/edit-profile", prompt.TeamProfileUrl);
        Assert.Equal("/account/onboarding/team-opportunities/" + teamId + "/new?type=tryout", prompt.CreateListingUrl);
        Assert.Equal("support@tryoutspot.com", prompt.SupportEmail);
    }

    [Fact]
    public async Task ActivationAssistance_Dismiss_SuppressesPrompt()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("activation-api-dismiss@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var teamId = await SeedManagedTeamAsync(factory, user.Id, completeProfile: false);
        await BackdateUserAsync(factory, user.Id);
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        var dismissResponse = await client.PostAsJsonAsync(
            "/api/dashboard/activation-assistance/dismiss",
            new DismissActivationAssistanceRequest
            {
                PromptKey = ActivationAssistancePromptKeys.TeamFirstListing,
                TeamId = teamId
            });

        Assert.Equal(HttpStatusCode.OK, dismissResponse.StatusCode);

        var response = await client.GetAsync("/api/dashboard/activation-assistance");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var prompt = await response.Content.ReadFromJsonAsync<ActivationAssistancePromptResponse>();
        Assert.NotNull(prompt);
        Assert.False(prompt.ShouldShow);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assistanceEvent = await dbContext.ActivationAssistanceEvents.SingleAsync();
        Assert.Equal(user.Id, assistanceEvent.UserId);
        Assert.Equal(teamId, assistanceEvent.TeamId);
        Assert.Equal("dismissed", assistanceEvent.EventType);
    }

    [Fact]
    public async Task ActivationAssistance_WithCompleteTeamProfile_ReturnsNoPrompt()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("activation-api-complete@example.com", [TryOutSpotRoles.TeamRepresentative]);
        await SeedManagedTeamAsync(factory, user.Id, completeProfile: true);
        await BackdateUserAsync(factory, user.Id);
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        var response = await client.GetAsync("/api/dashboard/activation-assistance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var prompt = await response.Content.ReadFromJsonAsync<ActivationAssistancePromptResponse>();
        Assert.NotNull(prompt);
        Assert.False(prompt.ShouldShow);
    }

    private static async Task<Guid> SeedManagedTeamAsync(
        TryOutSpotWebApplicationFactory factory,
        Guid userId,
        bool completeProfile)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var teamId = Guid.NewGuid();
        var team = new Team
        {
            Id = teamId,
            Name = "Activation Assist Aces",
            TeamLevel = completeProfile ? "14U" : null,
            GeographicScope = "Regional",
            City = completeProfile ? "Wichita" : null,
            State = completeProfile ? "KS" : null,
            ZipCode = completeProfile ? "67202" : null,
            Email = completeProfile ? "coach@example.com" : null,
            IsSearchable = true,
            IsContactInfoVisible = true,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };
        dbContext.Teams.Add(team);
        dbContext.UserTeamRoles.Add(new UserTeamRole
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TeamId = teamId,
            Role = TryOutSpotRoles.TeamRepresentative,
            StartDate = now,
            IsActive = true,
            CreatedAt = now
        });

        if (completeProfile)
        {
            var sportId = await dbContext.Sports
                .Where(sport => sport.IsActive && sport.Name == "Baseball")
                .Select(sport => sport.Id)
                .SingleAsync();
            dbContext.TeamSports.Add(new TeamSport
            {
                Id = Guid.NewGuid(),
                TeamId = teamId,
                SportId = sportId,
                IsActive = true,
                CreatedAt = now
            });
        }

        await dbContext.SaveChangesAsync();
        return teamId;
    }

    private static async Task BackdateUserAsync(TryOutSpotWebApplicationFactory factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await dbContext.Users.SingleAsync(currentUser => currentUser.Id == userId);
        user.CreatedAt = DateTime.UtcNow.AddDays(-2);
        await dbContext.SaveChangesAsync();
    }

    private static async Task<HttpClient> CreateAuthorizedClientAsync(
        TryOutSpotWebApplicationFactory factory,
        string email)
    {
        var client = factory.CreateClient();
        var loginResponse = await client.PostAsJsonAsync(
            "/api/account/login",
            new LoginRequest
            {
                Email = email,
                Password = "Tryout2026"
            });
        loginResponse.EnsureSuccessStatusCode();

        var token = await loginResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        Assert.NotNull(token);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            token.TokenType,
            token.AccessToken);
        return client;
    }
}
