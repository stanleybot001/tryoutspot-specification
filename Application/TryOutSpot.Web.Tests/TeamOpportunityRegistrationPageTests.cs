using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Listings;

namespace TryOutSpot.Web.Tests;

public sealed class TeamOpportunityRegistrationPageTests
{
    [Fact]
    public async Task ParentCanRegisterManagedPlayerForPublishedTryout()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var parentUser = await factory.CreateUserAsync("parent-register-page@example.com", [TryOutSpotRoles.Parent]);
        var seeded = SeedPublishedTryoutOpportunity(factory, parentUser.Id, fillToCapacity: false);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, parentUser.Email!);

        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/account/settings");
        var response = await client.PostAsync(
            $"/opportunities/{seeded.OpportunityId}/register",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", antiForgeryToken),
                new("RegistrationForm.PlayerId", seeded.ManagedPlayerId.ToString()),
                new("RegistrationForm.GuardianEmail", "parent-register-page@example.com"),
                new("RegistrationForm.WaiverAcknowledged", "true"),
                new("RegistrationForm.WaiverSignerName", "Parent Tester")
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/opportunities/{seeded.OpportunityId}", response.Headers.Location?.ToString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var registration = await dbContext.Registrations
            .SingleAsync(current =>
                current.OpportunityId == seeded.OpportunityId
                && current.PlayerId == seeded.ManagedPlayerId);

        Assert.Equal("pending", registration.Status);
        Assert.True(registration.WaiverSigned);
        Assert.Equal("Parent Tester", registration.WaiverSignerName);
        using var registrationDataDocument = JsonDocument.Parse(registration.RegistrationData ?? "{}");
        var registrationData = registrationDataDocument.RootElement;
        Assert.Equal("parent-register-page@example.com", registrationData.GetProperty("guardianEmail").GetString());
        Assert.Equal("Managed Player", registrationData.GetProperty("playerName").GetString());
        Assert.Equal("2012-01-15", registrationData.GetProperty("playerBirthDate").GetString());
        Assert.Equal("Central High", registrationData.GetProperty("playerSchool").GetString());
        Assert.Equal("555-222-1111", registrationData.GetProperty("playerPhone").GetString());
        Assert.Equal("managed.player@example.com", registrationData.GetProperty("playerEmail").GetString());
    }

    [Fact]
    public async Task AnonymousTryoutPage_ShowsCreateAccountAndSignInRegistrationActions()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var parentUser = await factory.CreateUserAsync("parent-register-cta@example.com", [TryOutSpotRoles.Parent]);
        var seeded = SeedPublishedTryoutOpportunity(factory, parentUser.Id, fillToCapacity: false);
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/opportunities/{seeded.OpportunityId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Create Free Account to Register", html, StringComparison.Ordinal);
        Assert.Contains("Sign in to register", html, StringComparison.Ordinal);
        Assert.Contains($"/account/register?returnUrl=%2Fopportunities%2F{seeded.OpportunityId}", html, StringComparison.Ordinal);
        Assert.Contains($"/account/login?returnUrl=%2Fopportunities%2F{seeded.OpportunityId}", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegistrationIsBlockedWhenOpportunityIsAtMaxCapacity()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var parentUser = await factory.CreateUserAsync("parent-register-capacity@example.com", [TryOutSpotRoles.Parent]);
        var seeded = SeedPublishedTryoutOpportunity(factory, parentUser.Id, fillToCapacity: false);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, parentUser.Email!);

        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/account/settings");
        FillOpportunityCapacity(factory, seeded.OpportunityId, parentUser.Id);
        var response = await client.PostAsync(
            $"/opportunities/{seeded.OpportunityId}/register",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", antiForgeryToken),
                new("RegistrationForm.PlayerId", seeded.ManagedPlayerId.ToString()),
                new("RegistrationForm.GuardianEmail", "parent-register-capacity@example.com"),
                new("RegistrationForm.WaiverAcknowledged", "true"),
                new("RegistrationForm.WaiverSignerName", "Capacity Parent")
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var detailResponse = await client.GetAsync($"/opportunities/{seeded.OpportunityId}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var html = await detailResponse.Content.ReadAsStringAsync();
        Assert.Contains("reached max capacity", html, StringComparison.OrdinalIgnoreCase);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var registrationsForManagedPlayer = await dbContext.Registrations
            .Where(current =>
                current.OpportunityId == seeded.OpportunityId
                && current.PlayerId == seeded.ManagedPlayerId)
            .ToArrayAsync();
        Assert.Empty(registrationsForManagedPlayer);
    }

    [Fact]
    public async Task TeamRepresentativeCanAcceptRegistrationFromTeamOpportunityPage()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var teamUser = await factory.CreateUserAsync("team-reviewer@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var seeded = SeedTeamOpportunityRegistrationForReview(factory, teamUser.Id);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, teamUser.Email!);

        var antiForgeryToken = await GetAntiForgeryTokenAsync(
            client,
            $"/account/onboarding/team-opportunities/{seeded.TeamId}");
        var response = await client.PostAsync(
            $"/account/onboarding/team-opportunities/{seeded.TeamId}/{seeded.OpportunityId}/registrations/{seeded.RegistrationId}/status",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", antiForgeryToken),
                new("status", "accepted")
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var registration = await dbContext.Registrations
            .SingleAsync(current => current.Id == seeded.RegistrationId);
        Assert.Equal("accepted", registration.Status);
    }

    [Fact]
    public async Task TeamRepresentativeOpportunityPage_CollapsesRosterAndMarksFavoritedPlayers()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var teamUser = await factory.CreateUserAsync("team-roster-favorite@example.com", [TryOutSpotRoles.TeamRepresentative]);
        await AddSubscriptionAsync(factory, teamUser.Id, TryOutSpotPlanCodes.TeamBasic);
        var followerOne = await factory.CreateUserAsync("team-roster-follower-one@example.com", [TryOutSpotRoles.Parent]);
        var followerTwo = await factory.CreateUserAsync("team-roster-follower-two@example.com", [TryOutSpotRoles.Parent]);
        var seeded = SeedTeamOpportunityRegistrationForReview(
            factory,
            teamUser.Id,
            favoritePlayer: true,
            opportunityFavoriteUserIds: [followerOne.Id, followerTwo.Id]);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, teamUser.Email!);

        var response = await client.GetAsync($"/account/onboarding/team-opportunities/{seeded.TeamId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("registration-roster-toggle", html, StringComparison.Ordinal);
        Assert.Contains("registration-roster-table", html, StringComparison.Ordinal);
        Assert.Contains("Check-in sheet", html, StringComparison.Ordinal);
        Assert.Contains("Evaluation sheet", html, StringComparison.Ordinal);
        Assert.Contains("Favorited player listing", html, StringComparison.Ordinal);
        Assert.Contains("<span>Followers: <strong class=\"text-body\">2</strong></span>", html, StringComparison.Ordinal);
        Assert.Contains("Pending Registrant", html, StringComparison.Ordinal);
        Assert.Contains(">1</strong>", html, StringComparison.Ordinal);
        Assert.Contains("Mark present", html, StringComparison.Ordinal);
        Assert.Contains("Mark waiver", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TeamRepresentativeCanOpenPrintableCheckInRoster()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var teamUser = await factory.CreateUserAsync("team-roster-share@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var seeded = SeedTeamOpportunityRegistrationForReview(factory, teamUser.Id, favoritePlayer: true);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, teamUser.Email!);

        var response = await client.GetAsync(
            $"/account/onboarding/team-opportunities/{seeded.TeamId}/{seeded.OpportunityId}/registrations/check-in");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Tryout check-in roster", html, StringComparison.Ordinal);
        Assert.Contains("Print / Save PDF", html, StringComparison.Ordinal);
        Assert.Contains("Email link", html, StringComparison.Ordinal);
        Assert.Contains("sms:?body=", html, StringComparison.Ordinal);
        Assert.Contains("Tryout #", html, StringComparison.Ordinal);
        Assert.Contains("Pending Registrant", html, StringComparison.Ordinal);
        Assert.Contains("North High", html, StringComparison.Ordinal);
        Assert.Contains("guardian-share@example.com", html, StringComparison.Ordinal);
        Assert.Contains("Favorited player listing", html, StringComparison.Ordinal);
        Assert.Contains(">1</td>", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TeamRepresentativeCanOpenPrintableEvaluationSheet()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var teamUser = await factory.CreateUserAsync("team-evaluation-share@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var seeded = SeedTeamOpportunityRegistrationForReview(factory, teamUser.Id, favoritePlayer: true);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, teamUser.Email!);

        var response = await client.GetAsync(
            $"/account/onboarding/team-opportunities/{seeded.TeamId}/{seeded.OpportunityId}/registrations/evaluation");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Coach evaluation sheet", html, StringComparison.Ordinal);
        Assert.Contains("Print / Save PDF", html, StringComparison.Ordinal);
        Assert.Contains("Tryout #", html, StringComparison.Ordinal);
        Assert.Contains("60 time", html, StringComparison.Ordinal);
        Assert.Contains("Hitting (1-5)", html, StringComparison.Ordinal);
        Assert.Contains("Fielding (1-5)", html, StringComparison.Ordinal);
        Assert.Contains("Pending Registrant", html, StringComparison.Ordinal);
        Assert.Contains(">1</td>", html, StringComparison.Ordinal);
    }

    private static (Guid OpportunityId, Guid ManagedPlayerId) SeedPublishedTryoutOpportunity(
        TryOutSpotWebApplicationFactory factory,
        Guid parentUserId,
        bool fillToCapacity)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var sportId = dbContext.Sports
            .Where(sport => sport.IsActive && sport.Name == "Softball")
            .Select(sport => sport.Id)
            .Single();

        var teamId = Guid.NewGuid();
        dbContext.Teams.Add(new Team
        {
            Id = teamId,
            Name = "Registration Test Team",
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

        var managedPlayerId = Guid.NewGuid();
        dbContext.Players.Add(new Player
        {
            Id = managedPlayerId,
            FirstName = "Managed",
            LastName = "Player",
            DateOfBirth = new DateTime(2012, 1, 15, 0, 0, 0, DateTimeKind.Utc),
            SchoolName = "Central High",
            ContactPhone = "555-222-1111",
            ContactEmail = "managed.player@example.com",
            ContactVisibility = "VerifiedCoachesOnly",
            IsSearchable = true,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        });

        dbContext.UserPlayerRelationships.Add(new UserPlayerRelationship
        {
            Id = Guid.NewGuid(),
            UserId = parentUserId,
            PlayerId = managedPlayerId,
            Relationship = "Parent",
            CanManage = true,
            CreatedAt = now
        });

        var opportunityId = Guid.NewGuid();
        dbContext.Opportunities.Add(new Opportunity
        {
            Id = opportunityId,
            TeamId = teamId,
            SportId = sportId,
            Type = "tryout",
            Title = "Registration Workflow Tryout",
            RegistrationRequired = true,
            RegistrationFee = 30m,
            RegistrationRequiredFieldCodes = JsonSerializer.Serialize(new[]
            {
                "player_name",
                "player_birthdate",
                "player_school",
                "player_phone",
                "player_email",
                "guardian_email",
                "waiver_signature"
            }),
            MaxParticipants = 1,
            RegistrationDeadline = now.AddDays(5),
            EventDate = now.AddDays(7),
            City = "McPherson",
            State = "KS",
            ZipCode = "67460",
            IsPublished = true,
            PublishedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        });

        if (fillToCapacity)
        {
            var otherPlayerId = Guid.NewGuid();
            dbContext.Players.Add(new Player
            {
                Id = otherPlayerId,
                FirstName = "Already",
                LastName = "Registered",
                DateOfBirth = now.AddYears(-15),
                ContactVisibility = "VerifiedCoachesOnly",
                IsSearchable = true,
                CreatedAt = now,
                UpdatedAt = now,
                IsActive = true
            });

            dbContext.Registrations.Add(new Registration
            {
                Id = Guid.NewGuid(),
                OpportunityId = opportunityId,
                PlayerId = otherPlayerId,
                RegisteredByUserId = parentUserId,
                Status = "pending",
                RegistrationData = "{}",
                PaymentStatus = "in_person",
                Amount = 30m,
                WaiverSigned = true,
                WaiverSignedAt = now,
                WaiverSignerName = "Existing Parent",
                AttendanceStatus = "pending",
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        dbContext.SaveChanges();
        return (opportunityId, managedPlayerId);
    }

    private static void FillOpportunityCapacity(
        TryOutSpotWebApplicationFactory factory,
        Guid opportunityId,
        Guid registeredByUserId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        var otherPlayerId = Guid.NewGuid();
        dbContext.Players.Add(new Player
        {
            Id = otherPlayerId,
            FirstName = "Already",
            LastName = "Registered",
            DateOfBirth = now.AddYears(-15),
            ContactVisibility = "VerifiedCoachesOnly",
            IsSearchable = true,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        });

        dbContext.Registrations.Add(new Registration
        {
            Id = Guid.NewGuid(),
            OpportunityId = opportunityId,
            PlayerId = otherPlayerId,
            RegisteredByUserId = registeredByUserId,
            Status = "pending",
            RegistrationData = "{}",
            PaymentStatus = "in_person",
            Amount = 30m,
            WaiverSigned = true,
            WaiverSignedAt = now,
            WaiverSignerName = "Existing Parent",
            AttendanceStatus = "pending",
            CreatedAt = now,
            UpdatedAt = now
        });

        dbContext.SaveChanges();
    }

    private static (Guid TeamId, Guid OpportunityId, Guid RegistrationId, Guid PlayerId) SeedTeamOpportunityRegistrationForReview(
        TryOutSpotWebApplicationFactory factory,
        Guid teamUserId,
        bool favoritePlayer = false,
        IReadOnlyCollection<Guid>? opportunityFavoriteUserIds = null)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var sportId = dbContext.Sports
            .Where(sport => sport.IsActive && sport.Name == "Softball")
            .Select(sport => sport.Id)
            .Single();

        var teamId = Guid.NewGuid();
        dbContext.Teams.Add(new Team
        {
            Id = teamId,
            Name = "Review Status Team",
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

        dbContext.UserTeamRoles.Add(new UserTeamRole
        {
            Id = Guid.NewGuid(),
            UserId = teamUserId,
            TeamId = teamId,
            Role = TryOutSpotRoles.TeamRepresentative,
            IsActive = true,
            CreatedAt = now
        });

        var playerId = Guid.NewGuid();
        dbContext.Players.Add(new Player
        {
            Id = playerId,
            FirstName = "Pending",
            LastName = "Registrant",
            DateOfBirth = new DateTime(2011, 4, 10, 0, 0, 0, DateTimeKind.Utc),
            SchoolName = "North High",
            ContactPhone = "555-555-1200",
            ContactEmail = "pending.registrant@example.com",
            ContactVisibility = "VerifiedCoachesOnly",
            IsSearchable = true,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        });

        var opportunityId = Guid.NewGuid();
        dbContext.Opportunities.Add(new Opportunity
        {
            Id = opportunityId,
            TeamId = teamId,
            SportId = sportId,
            Type = "tryout",
            Title = "Team Review Tryout",
            RegistrationRequired = true,
            RegistrationFee = 0m,
            EventDate = now.AddDays(10),
            City = "McPherson",
            State = "KS",
            ZipCode = "67460",
            IsPublished = true,
            PublishedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        });

        var registrationId = Guid.NewGuid();
        dbContext.Registrations.Add(new Registration
        {
            Id = registrationId,
            OpportunityId = opportunityId,
            PlayerId = playerId,
            RegisteredByUserId = teamUserId,
            Status = "pending",
            RegistrationData = JsonSerializer.Serialize(new
            {
                playerPhone = "555-555-1200",
                playerEmail = "pending.registrant@example.com",
                guardianName = "Guardian Share",
                guardianEmail = "guardian-share@example.com",
                guardianPhone = "555-555-1201"
            }),
            PaymentStatus = "not_required",
            WaiverSigned = false,
            EmergencyContactName = "Emergency Share",
            EmergencyContactPhone = "555-555-1202",
            AttendanceStatus = "pending",
            CreatedAt = now,
            UpdatedAt = now
        });

        if (favoritePlayer)
        {
            var listingId = Guid.NewGuid();
            dbContext.PlayerListings.Add(new PlayerListing
            {
                Id = listingId,
                UserId = teamUserId,
                PlayerId = playerId,
                SportId = sportId,
                ListingType = TryOutSpotPlayerListingTypes.PickupPlayer,
                Title = "Favorite pending registrant",
                City = "McPherson",
                State = "KS",
                ZipCode = "67460",
                IsPublished = true,
                IsSearchable = true,
                PublishedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                IsActive = true
            });
            dbContext.UserFavorites.Add(new UserFavorite
            {
                Id = Guid.NewGuid(),
                UserId = teamUserId,
                PlayerListingId = listingId,
                CreatedAt = now
            });
        }

        var followerIndex = 0;
        foreach (var followerUserId in (opportunityFavoriteUserIds ?? []).Distinct())
        {
            dbContext.UserFavorites.Add(new UserFavorite
            {
                Id = Guid.NewGuid(),
                UserId = followerUserId,
                OpportunityId = opportunityId,
                CreatedAt = now.AddMinutes(followerIndex++)
            });
        }

        dbContext.SaveChanges();
        return (teamId, opportunityId, registrationId, playerId);
    }

    private static async Task<string> GetAntiForgeryTokenAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);

        return match.Success
            ? match.Groups[1].Value
            : throw new InvalidOperationException($"No anti-forgery token was found on {path}.");
    }

    private static async Task LoginWebUserAsync(HttpClient client, string email)
    {
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/account/login");
        var loginResponse = await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", antiForgeryToken),
                new("Email", email),
                new("Password", "Tryout2026"),
                new("RememberMe", "true")
            ]));

        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
    }

    private static async Task AddSubscriptionAsync(
        TryOutSpotWebApplicationFactory factory,
        Guid userId,
        string planCode)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        dbContext.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PlanType = planCode,
            Status = "active",
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
