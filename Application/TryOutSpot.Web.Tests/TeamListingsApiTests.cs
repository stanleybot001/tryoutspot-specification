using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Models.Account;
using TryOutSpot.Web.Models.TeamListings;

namespace TryOutSpot.Web.Tests;

public sealed class TeamListingsApiTests
{
    [Fact]
    public async Task CoachWithBasicTeamPlan_CanPublishUpToFiveOpportunitiesPerMonth()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("team-basic@example.com", [TryOutSpotRoles.Coach]);
        await AddSubscriptionAsync(factory, user.Id, TryOutSpotPlanCodes.TeamBasic, "active");
        var sportId = GetActiveSportId(factory, "Softball");
        var teamId = SeedManagedTeam(factory, user.Id, sportId, "Basic Team Aces");
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        for (var index = 1; index <= 5; index++)
        {
            var createResponse = await client.PostAsJsonAsync(
                $"/api/team-listings/mine/{teamId}/opportunities",
                new CreateTeamOpportunityRequest
                {
                    Type = "tryout",
                    Title = $"Open tryout #{index}",
                    SportId = sportId,
                    RegistrationFee = 20m,
                    EventDate = DateTime.UtcNow.AddDays(10 + index),
                    IsPublished = true
                });

            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        }

        var overLimitResponse = await client.PostAsJsonAsync(
            $"/api/team-listings/mine/{teamId}/opportunities",
            new CreateTeamOpportunityRequest
            {
                Type = "tryout",
                Title = "Open tryout #6",
                SportId = sportId,
                RegistrationFee = 20m,
                EventDate = DateTime.UtcNow.AddDays(30),
                IsPublished = true
            });

        Assert.Equal(HttpStatusCode.BadRequest, overLimitResponse.StatusCode);
        var responseBody = await overLimitResponse.Content.ReadAsStringAsync();
        Assert.Contains("up to 5 published opportunities per month", responseBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CoachWithProfessionalPlan_CanPublishMoreThanFiveOpportunitiesPerMonth()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("team-pro@example.com", [TryOutSpotRoles.Coach]);
        await AddSubscriptionAsync(factory, user.Id, TryOutSpotPlanCodes.TeamProfessional, "active", BillingIntervalCodes.Year);
        var sportId = GetActiveSportId(factory, "Softball");
        var teamId = SeedManagedTeam(factory, user.Id, sportId, "Pro Team Thunder");
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        for (var index = 1; index <= 6; index++)
        {
            var createResponse = await client.PostAsJsonAsync(
                $"/api/team-listings/mine/{teamId}/opportunities",
                new CreateTeamOpportunityRequest
                {
                    Type = "tryout",
                    Title = $"Pro tryout #{index}",
                    SportId = sportId,
                    RegistrationFee = 30m,
                    EventDate = DateTime.UtcNow.AddDays(15 + index),
                    IsPublished = true
                });

            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        }
    }

    [Fact]
    public async Task CoachWithoutTeamPostingPlan_CannotCreateOpportunity()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("team-no-plan@example.com", [TryOutSpotRoles.Coach]);
        var sportId = GetActiveSportId(factory, "Softball");
        var teamId = SeedManagedTeam(factory, user.Id, sportId, "No Plan Team");
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        var response = await client.PostAsJsonAsync(
            $"/api/team-listings/mine/{teamId}/opportunities",
            new CreateTeamOpportunityRequest
            {
                Type = "tryout",
                Title = "No plan listing attempt",
                SportId = sportId,
                RegistrationFee = 10m,
                EventDate = DateTime.UtcNow.AddDays(14),
                IsPublished = true
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static Guid GetActiveSportId(TryOutSpotWebApplicationFactory factory, string sportName)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return dbContext.Sports
            .Where(sport => sport.IsActive && sport.Name == sportName)
            .Select(sport => sport.Id)
            .Single();
    }

    private static Guid SeedManagedTeam(
        TryOutSpotWebApplicationFactory factory,
        Guid userId,
        Guid sportId,
        string teamName)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var teamId = Guid.NewGuid();
        dbContext.Teams.Add(new Team
        {
            Id = teamId,
            Name = teamName,
            TeamLevel = "14U",
            GeographicScope = "Local",
            City = "McPherson",
            State = "KS",
            ZipCode = "67460",
            IsSearchable = true,
            IsContactInfoVisible = true,
            IsElite = false,
            IsVerified = false,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        });

        dbContext.TeamSports.Add(new TeamSport
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            SportId = sportId,
            IsActive = true,
            CreatedAt = now
        });

        dbContext.UserTeamRoles.Add(new UserTeamRole
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TeamId = teamId,
            Role = TryOutSpotRoles.Coach,
            StartDate = now,
            IsActive = true,
            CreatedAt = now
        });

        dbContext.SaveChanges();
        return teamId;
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
        string status,
        string billingInterval = BillingIntervalCodes.Month)
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
            CurrentPeriodEnd = billingInterval == BillingIntervalCodes.Year ? now.AddYears(1) : now.AddMonths(1),
            Amount = 29m,
            Currency = "USD",
            BillingInterval = billingInterval,
            CreatedAt = now,
            UpdatedAt = now
        });

        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
    }
}
