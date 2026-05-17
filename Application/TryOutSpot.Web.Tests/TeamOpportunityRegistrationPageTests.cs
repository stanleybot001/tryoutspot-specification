using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;

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

    private static (Guid TeamId, Guid OpportunityId, Guid RegistrationId) SeedTeamOpportunityRegistrationForReview(
        TryOutSpotWebApplicationFactory factory,
        Guid teamUserId)
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
            RegistrationData = "{}",
            PaymentStatus = "not_required",
            WaiverSigned = false,
            AttendanceStatus = "pending",
            CreatedAt = now,
            UpdatedAt = now
        });

        dbContext.SaveChanges();
        return (teamId, opportunityId, registrationId);
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
}
