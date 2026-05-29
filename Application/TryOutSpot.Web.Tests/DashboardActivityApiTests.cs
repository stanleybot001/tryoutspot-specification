using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Listings;
using TryOutSpot.Web.Models.Account;
using TryOutSpot.Web.Models.Dashboard;

namespace TryOutSpot.Web.Tests;

public sealed class DashboardActivityApiTests
{
    [Fact]
    public async Task RecentActivity_FreeParent_OnlyIncludesTryouts()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("dashboard-free-parent@example.com", [TryOutSpotRoles.Parent]);
        var listingOwner = await factory.CreateUserAsync("dashboard-free-listing-owner@example.com", [TryOutSpotRoles.Parent]);
        SeedPlayerParentActivity(factory, listingOwner.Id);
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        var response = await client.GetAsync("/api/dashboard/recent-activity");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DashboardRecentActivityResponse>();
        Assert.NotNull(payload);
        var titles = payload.Sections.SelectMany(section => section.Items).Select(item => item.Title).ToArray();
        Assert.Contains("Free visible tryout", titles);
        Assert.DoesNotContain("Premium tournament listing", titles);
        Assert.DoesNotContain("Used catcher gear", titles);
        Assert.Equal([DashboardActivityTypeCodes.Tryouts], payload.EffectiveActivityTypes);

        var preferencesResponse = await client.GetAsync("/api/dashboard/preferences");
        Assert.Equal(HttpStatusCode.OK, preferencesResponse.StatusCode);
        var preferences = await preferencesResponse.Content.ReadFromJsonAsync<DashboardActivityPreferencesResponse>();
        Assert.NotNull(preferences);
        Assert.Contains(preferences.Options, option => option.Code == DashboardActivityTypeCodes.Tryouts && option.IsAvailable);
        Assert.Contains(preferences.Options, option => option.Code == DashboardActivityTypeCodes.Tournaments && !option.IsAvailable);
    }

    [Fact]
    public async Task RecentActivity_PremiumParent_HonorsSavedPreferenceFilter()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("dashboard-premium-parent@example.com", [TryOutSpotRoles.Parent]);
        await AddSubscriptionAsync(factory, user.Id, TryOutSpotPlanCodes.PremiumPlayer, "active");
        var listingOwner = await factory.CreateUserAsync("dashboard-premium-listing-owner@example.com", [TryOutSpotRoles.Parent]);
        SeedPlayerParentActivity(factory, listingOwner.Id);
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        var updateResponse = await client.PostAsJsonAsync(
            "/api/dashboard/preferences",
            new UpdateDashboardActivityPreferencesRequest
            {
                ActivityTypes = [DashboardActivityTypeCodes.ForSaleItems]
            });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var preferences = await updateResponse.Content.ReadFromJsonAsync<DashboardActivityPreferencesResponse>();
        Assert.NotNull(preferences);
        Assert.Equal([DashboardActivityTypeCodes.ForSaleItems], preferences.SelectedActivityTypes);

        var response = await client.GetAsync("/api/dashboard/recent-activity");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DashboardRecentActivityResponse>();
        Assert.NotNull(payload);
        var titles = payload.Sections.SelectMany(section => section.Items).Select(item => item.Title).ToArray();
        Assert.Contains("Used catcher gear", titles);
        Assert.DoesNotContain("Free visible tryout", titles);
        Assert.DoesNotContain("Premium tournament listing", titles);
    }

    [Fact]
    public async Task RecentActivity_TeamRepresentative_IncludesNewPlayerListingsOnly()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var teamUser = await factory.CreateUserAsync("dashboard-team@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var parentUser = await factory.CreateUserAsync("dashboard-player-owner@example.com", [TryOutSpotRoles.Parent]);
        SeedTeamActivity(factory, parentUser.Id);
        var client = await CreateAuthorizedClientAsync(factory, teamUser.Email!);

        var response = await client.GetAsync("/api/dashboard/recent-activity");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DashboardRecentActivityResponse>();
        Assert.NotNull(payload);
        var items = payload.Sections.SelectMany(section => section.Items).ToArray();
        Assert.DoesNotContain(items, item => item.Title == "Avery Blake");
        Assert.Contains(items, item => item.Title == "2027 shortstop looking for fall roster");
        Assert.Equal([DashboardActivityTypeCodes.TeamNewListings], payload.EffectiveActivityTypes);
    }

    [Fact]
    public async Task Preferences_TeamRepresentative_MapsLegacyNewPlayersPreferenceToListings()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var teamUser = await factory.CreateUserAsync("dashboard-team-legacy-preference@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var parentUser = await factory.CreateUserAsync("dashboard-player-owner-legacy-preference@example.com", [TryOutSpotRoles.Parent]);
        SeedTeamActivity(factory, parentUser.Id);
        var client = await CreateAuthorizedClientAsync(factory, teamUser.Email!);

        var updateResponse = await client.PostAsJsonAsync(
            "/api/dashboard/preferences",
            new UpdateDashboardActivityPreferencesRequest
            {
                ActivityTypes = ["team_new_players"]
            });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var preferences = await updateResponse.Content.ReadFromJsonAsync<DashboardActivityPreferencesResponse>();
        Assert.NotNull(preferences);
        Assert.Equal([DashboardActivityTypeCodes.TeamNewListings], preferences.SelectedActivityTypes);

        var response = await client.GetAsync("/api/dashboard/recent-activity");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<DashboardRecentActivityResponse>();
        Assert.NotNull(payload);
        Assert.Equal([DashboardActivityTypeCodes.TeamNewListings], payload.EffectiveActivityTypes);
        Assert.Contains(
            payload.Sections.SelectMany(section => section.Items),
            item => item.Title == "2027 shortstop looking for fall roster");
    }

    private static void SeedPlayerParentActivity(TryOutSpotWebApplicationFactory factory, Guid listingOwnerId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sportId = dbContext.Sports
            .Where(sport => sport.IsActive && sport.Name == "Softball")
            .Select(sport => sport.Id)
            .Single();
        var now = DateTime.UtcNow;
        var team = new Team
        {
            Id = Guid.NewGuid(),
            Name = "Dashboard Aces",
            TeamLevel = "14U",
            GeographicScope = "Regional",
            City = "McPherson",
            State = "KS",
            ZipCode = "67460",
            IsSearchable = true,
            IsContactInfoVisible = true,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };
        dbContext.Teams.Add(team);
        dbContext.Opportunities.AddRange(
            new Opportunity
            {
                Id = Guid.NewGuid(),
                TeamId = team.Id,
                SportId = sportId,
                Type = "tryout",
                Title = "Free visible tryout",
                RegistrationFee = 20m,
                EventDate = now.AddDays(14),
                IsPublished = true,
                PublishedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                IsActive = true
            },
            new Opportunity
            {
                Id = Guid.NewGuid(),
                TeamId = team.Id,
                SportId = sportId,
                Type = "tournament",
                Title = "Premium tournament listing",
                RegistrationFee = 125m,
                EventDate = now.AddDays(28),
                IsPublished = true,
                PublishedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                IsActive = true
            });
        dbContext.PlayerListings.Add(new PlayerListing
        {
            Id = Guid.NewGuid(),
            UserId = listingOwnerId,
            SportId = sportId,
            ListingType = TryOutSpotPlayerListingTypes.UsedEquipment,
            Title = "Used catcher gear",
            AskingPrice = 150m,
            Currency = "USD",
            City = "Wichita",
            State = "KS",
            ZipCode = "67202",
            IsPublished = true,
            IsSearchable = true,
            PublishedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        });

        dbContext.SaveChanges();
    }

    private static void SeedTeamActivity(TryOutSpotWebApplicationFactory factory, Guid ownerId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sportId = dbContext.Sports
            .Where(sport => sport.IsActive && sport.Name == "Softball")
            .Select(sport => sport.Id)
            .Single();
        var now = DateTime.UtcNow;
        var playerId = Guid.NewGuid();
        dbContext.Players.Add(new Player
        {
            Id = playerId,
            FirstName = "Avery",
            LastName = "Blake",
            DateOfBirth = new DateTime(2010, 8, 4, 0, 0, 0, DateTimeKind.Utc),
            SchoolName = "Central High",
            ContactVisibility = "VerifiedCoachesOnly",
            City = "Wichita",
            State = "KS",
            ZipCode = "67202",
            IsSearchable = true,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        });
        dbContext.PlayerSports.Add(new PlayerSport
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            SportId = sportId,
            PrimaryPosition = "Shortstop",
            SkillLevel = "Advanced",
            IsActive = true,
            CreatedAt = now
        });
        dbContext.UserPlayerRelationships.Add(new UserPlayerRelationship
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            PlayerId = playerId,
            Relationship = "Parent",
            CanManage = true,
            CreatedAt = now
        });
        dbContext.PlayerListings.Add(new PlayerListing
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            PlayerId = playerId,
            SportId = sportId,
            ListingType = TryOutSpotPlayerListingTypes.LookingForTeam,
            Title = "2027 shortstop looking for fall roster",
            City = "Wichita",
            State = "KS",
            ZipCode = "67202",
            IsPublished = true,
            IsSearchable = true,
            PublishedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        });

        dbContext.SaveChanges();
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

    private static async Task AddSubscriptionAsync(
        TryOutSpotWebApplicationFactory factory,
        Guid userId,
        string planCode,
        string status)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        dbContext.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PlanType = planCode,
            Status = status,
            ScopeType = TryOutSpotSubscriptionScopeTypes.Account,
            ScopeId = null,
            StripeCustomerId = $"cus_{Guid.NewGuid():N}",
            StripeSubscriptionId = $"sub_{Guid.NewGuid():N}",
            CurrentPeriodStart = now,
            CurrentPeriodEnd = now.AddMonths(1),
            Amount = 29m,
            Currency = "USD",
            BillingInterval = BillingIntervalCodes.Month,
            CreatedAt = now,
            UpdatedAt = now
        });

        await dbContext.SaveChangesAsync();
    }
}
