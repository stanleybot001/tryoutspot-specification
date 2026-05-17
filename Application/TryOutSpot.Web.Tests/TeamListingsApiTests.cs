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
using TryOutSpot.Web.Models.TeamListings;

namespace TryOutSpot.Web.Tests;

public sealed class TeamListingsApiTests
{
    [Fact]
    public async Task CoachWithFreePlan_CanPublishOneOpportunityEverySixMonths()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("team-free@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var sportId = GetActiveSportId(factory, "Softball");
        var teamId = SeedManagedTeam(factory, user.Id, sportId, "Free Coach Aces");
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        var createResponse = await client.PostAsJsonAsync(
            $"/api/team-listings/mine/{teamId}/opportunities",
            new CreateTeamOpportunityRequest
            {
                Type = "tryout",
                Title = "Free Coach tryout #1",
                SportId = sportId,
                RegistrationFee = 20m,
                EventDate = DateTime.UtcNow.AddDays(10),
                IsPublished = true
            });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var overLimitResponse = await client.PostAsJsonAsync(
            $"/api/team-listings/mine/{teamId}/opportunities",
            new CreateTeamOpportunityRequest
            {
                Type = "tryout",
                Title = "Free Coach tryout #2",
                SportId = sportId,
                RegistrationFee = 20m,
                EventDate = DateTime.UtcNow.AddDays(30),
                IsPublished = true
            });

        Assert.Equal(HttpStatusCode.BadRequest, overLimitResponse.StatusCode);
        var responseBody = await overLimitResponse.Content.ReadAsStringAsync();
        Assert.Contains("up to 1 published opportunities every 6 months", responseBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CoachWithBasicTeamPlan_CanPublishUpToNineOpportunitiesPerTwelveMonths()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("team-basic@example.com", [TryOutSpotRoles.TeamRepresentative]);
        await AddSubscriptionAsync(factory, user.Id, TryOutSpotPlanCodes.TeamBasic, "active");
        var sportId = GetActiveSportId(factory, "Softball");
        var teamId = SeedManagedTeam(factory, user.Id, sportId, "Basic Team Aces");
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        for (var index = 1; index <= 9; index++)
        {
            var createResponse = await client.PostAsJsonAsync(
                $"/api/team-listings/mine/{teamId}/opportunities",
                new CreateTeamOpportunityRequest
                {
                    Type = "tryout",
                    Title = $"Basic tryout #{index}",
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
                Title = "Basic tryout #10",
                SportId = sportId,
                RegistrationFee = 20m,
                EventDate = DateTime.UtcNow.AddDays(30),
                IsPublished = true
            });

        Assert.Equal(HttpStatusCode.BadRequest, overLimitResponse.StatusCode);
        var responseBody = await overLimitResponse.Content.ReadAsStringAsync();
        Assert.Contains("up to 9 published opportunities every 12 months", responseBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CoachWithProfessionalPlan_CanPublishMoreThanNineOpportunitiesPerTwelveMonths()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("team-pro@example.com", [TryOutSpotRoles.TeamRepresentative]);
        await AddSubscriptionAsync(factory, user.Id, TryOutSpotPlanCodes.TeamProfessional, "active", BillingIntervalCodes.Year);
        var sportId = GetActiveSportId(factory, "Softball");
        var teamId = SeedManagedTeam(factory, user.Id, sportId, "Pro Team Thunder");
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        for (var index = 1; index <= 12; index++)
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
    public async Task ParentWithoutTeamRole_CannotCreateOpportunity()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("parent-no-team-role@example.com", [TryOutSpotRoles.Parent]);
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

    [Fact]
    public async Task BasicTeam_CanConfigureTryoutRegistrationFieldsAndCapacity()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("team-basic-registration@example.com", [TryOutSpotRoles.TeamRepresentative]);
        await AddSubscriptionAsync(factory, user.Id, TryOutSpotPlanCodes.TeamBasic, "active");
        var sportId = GetActiveSportId(factory, "Softball");
        var teamId = SeedManagedTeam(factory, user.Id, sportId, "Basic Team Registration Aces");
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        var response = await client.PostAsJsonAsync(
            $"/api/team-listings/mine/{teamId}/opportunities",
            new CreateTeamOpportunityRequest
            {
                Type = "tryout",
                Title = "Basic Team Registration Tryout",
                SportId = sportId,
                RegistrationRequired = true,
                RegistrationFee = 35m,
                MaxParticipants = 24,
                RequiredRegistrationFieldCodes =
                [
                    TryOutSpotOpportunityRegistrationFields.GuardianEmail,
                    TryOutSpotOpportunityRegistrationFields.WaiverSignature
                ],
                WaiverRequired = true,
                WaiverMethod = TryOutSpotOpportunityWaiverMethods.Downloadable,
                WaiverReturnByEmail = true,
                WaiverReturnInPerson = false,
                ContactEmail = "waivers@example.com",
                EventDate = DateTime.UtcNow.AddDays(14),
                IsPublished = true
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TeamOpportunityActionResponse>();
        Assert.NotNull(payload);
        Assert.True(payload.Opportunity.RegistrationRequired);
        Assert.Equal(24, payload.Opportunity.MaxParticipants);
        Assert.Contains(
            TryOutSpotOpportunityRegistrationFields.GuardianEmail,
            payload.Opportunity.RequiredRegistrationFieldCodes);
        Assert.Contains(
            TryOutSpotOpportunityRegistrationFields.WaiverSignature,
            payload.Opportunity.RequiredRegistrationFieldCodes);
        Assert.True(payload.Opportunity.WaiverRequired);
        Assert.Equal(TryOutSpotOpportunityWaiverMethods.Downloadable, payload.Opportunity.WaiverMethod);
        Assert.True(payload.Opportunity.WaiverReturnByEmail);
        Assert.False(payload.Opportunity.WaiverReturnInPerson);
    }

    [Fact]
    public async Task FreeCoach_CannotPersistTryoutRegistrationConfiguration()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("team-free-registration@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var sportId = GetActiveSportId(factory, "Softball");
        var teamId = SeedManagedTeam(factory, user.Id, sportId, "Free Coach Registration Aces");
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        var response = await client.PostAsJsonAsync(
            $"/api/team-listings/mine/{teamId}/opportunities",
            new CreateTeamOpportunityRequest
            {
                Type = "tryout",
                Title = "Free Coach Registration Tryout",
                SportId = sportId,
                RegistrationRequired = true,
                RegistrationFee = 15m,
                MaxParticipants = 12,
                RequiredRegistrationFieldCodes =
                [
                    TryOutSpotOpportunityRegistrationFields.GuardianEmail
                ],
                EventDate = DateTime.UtcNow.AddDays(7),
                IsPublished = true
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TeamOpportunityActionResponse>();
        Assert.NotNull(payload);
        Assert.False(payload.Opportunity.RegistrationRequired);
        Assert.Null(payload.Opportunity.MaxParticipants);
        Assert.Empty(payload.Opportunity.RequiredRegistrationFieldCodes);
    }

    [Fact]
    public async Task BasicTeam_NonTryoutRequest_WithRegistrationFields_IsSavedWithoutRegistration()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("team-basic-non-tryout-registration@example.com", [TryOutSpotRoles.TeamRepresentative]);
        await AddSubscriptionAsync(factory, user.Id, TryOutSpotPlanCodes.TeamBasic, "active");
        var sportId = GetActiveSportId(factory, "Softball");
        var teamId = SeedManagedTeam(factory, user.Id, sportId, "Basic Team Non-Tryout");
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        var response = await client.PostAsJsonAsync(
            $"/api/team-listings/mine/{teamId}/opportunities",
            new CreateTeamOpportunityRequest
            {
                Type = "pickup_player",
                Title = "Pickup player listing with registration toggles",
                SportId = sportId,
                RegistrationRequired = true,
                RegistrationFee = 25m,
                MaxParticipants = 15,
                RequiredRegistrationFieldCodes =
                [
                    TryOutSpotOpportunityRegistrationFields.PlayerEmail,
                    TryOutSpotOpportunityRegistrationFields.GuardianEmail
                ],
                WaiverRequired = true,
                WaiverMethod = TryOutSpotOpportunityWaiverMethods.AtEvent,
                EventDate = DateTime.UtcNow.AddDays(20),
                IsPublished = true
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TeamOpportunityActionResponse>();
        Assert.NotNull(payload);
        Assert.False(payload.Opportunity.RegistrationRequired);
        Assert.Null(payload.Opportunity.MaxParticipants);
        Assert.Empty(payload.Opportunity.RequiredRegistrationFieldCodes);
        Assert.False(payload.Opportunity.WaiverRequired);
        Assert.Null(payload.Opportunity.WaiverMethod);
    }

    [Fact]
    public async Task BasicTeam_AtEventWaiver_DoesNotRequireReturnOptions_AndPersistsRegistrationDates()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("team-basic-at-event-waiver@example.com", [TryOutSpotRoles.TeamRepresentative]);
        await AddSubscriptionAsync(factory, user.Id, TryOutSpotPlanCodes.TeamBasic, "active");
        var sportId = GetActiveSportId(factory, "Softball");
        var teamId = SeedManagedTeam(factory, user.Id, sportId, "Basic Team At Event Waiver");
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        var listingStartDate = DateTime.UtcNow.Date.AddDays(3);
        var listingEndDate = listingStartDate.AddDays(10);
        var eventDate = listingStartDate.AddDays(7);
        var eventEndDate = eventDate.AddDays(1);
        var registrationDeadline = eventDate.AddDays(-1);

        var response = await client.PostAsJsonAsync(
            $"/api/team-listings/mine/{teamId}/opportunities",
            new CreateTeamOpportunityRequest
            {
                Type = "tryout",
                Title = "At event waiver tryout",
                SportId = sportId,
                RegistrationRequired = true,
                RegistrationFee = 40m,
                MaxParticipants = 25,
                RequiredRegistrationFieldCodes =
                [
                    TryOutSpotOpportunityRegistrationFields.PlayerName,
                    TryOutSpotOpportunityRegistrationFields.PlayerEmail
                ],
                WaiverRequired = true,
                WaiverMethod = TryOutSpotOpportunityWaiverMethods.AtEvent,
                WaiverReturnByEmail = false,
                WaiverReturnInPerson = false,
                RegistrationDeadline = registrationDeadline,
                EventDate = eventDate,
                EventEndDate = eventEndDate,
                ListingStartDate = listingStartDate,
                ListingEndDate = listingEndDate,
                IsPublished = true
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<TeamOpportunityActionResponse>();
        Assert.NotNull(payload);
        Assert.True(payload.Opportunity.RegistrationRequired);
        Assert.Equal(25, payload.Opportunity.MaxParticipants);
        Assert.Contains(
            TryOutSpotOpportunityRegistrationFields.PlayerName,
            payload.Opportunity.RequiredRegistrationFieldCodes);
        Assert.Contains(
            TryOutSpotOpportunityRegistrationFields.PlayerEmail,
            payload.Opportunity.RequiredRegistrationFieldCodes);
        Assert.Contains(
            TryOutSpotOpportunityRegistrationFields.WaiverSignature,
            payload.Opportunity.RequiredRegistrationFieldCodes);
        Assert.True(payload.Opportunity.WaiverRequired);
        Assert.Equal(TryOutSpotOpportunityWaiverMethods.AtEvent, payload.Opportunity.WaiverMethod);
        Assert.False(payload.Opportunity.WaiverReturnByEmail);
        Assert.False(payload.Opportunity.WaiverReturnInPerson);
        Assert.Equal(listingStartDate.Date, payload.Opportunity.ListingStartDate?.Date);
        Assert.Equal(listingEndDate.Date, payload.Opportunity.ListingEndDate?.Date);
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
            Role = TryOutSpotRoles.TeamRepresentative,
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

