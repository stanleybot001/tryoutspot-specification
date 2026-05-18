using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Listings;

namespace TryOutSpot.Web.Tests;

public sealed class ListingReportPageTests
{
    [Fact]
    public async Task SignedInUsers_CanReportPlayerAndTeamListingsFromDetailPages()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var playerOwner = await factory.CreateUserAsync("report-ui-player-owner@example.com", [TryOutSpotRoles.Parent]);
        var teamOwner = await factory.CreateUserAsync("report-ui-team-owner@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var reporter = await factory.CreateUserAsync("report-ui-reporter@example.com", [TryOutSpotRoles.Parent]);
        var sportId = GetActiveSportId(factory);
        var playerId = SeedPlayerProfile(factory, playerOwner.Id, sportId);
        var playerListingId = SeedPlayerListing(factory, playerOwner.Id, sportId, playerId, "Report UI pickup listing");
        var (_, opportunityId) = SeedTeamOpportunity(factory, teamOwner.Id, sportId, "Report UI Aces", "Report UI tryout");
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        await LoginWebUserAsync(client, reporter.Email!);

        var playerPath = $"/player-listings/{playerListingId}";
        var playerPage = await client.GetAsync(playerPath);
        Assert.Equal(HttpStatusCode.OK, playerPage.StatusCode);
        var playerHtml = await playerPage.Content.ReadAsStringAsync();
        Assert.Contains("Report listing", playerHtml);
        Assert.Contains("Submit report", playerHtml);
        var playerReportResponse = await client.PostAsync(
            $"/player-listings/{playerListingId}/report",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", ExtractAntiForgeryToken(playerHtml, playerPath)),
                new("Reason", "Inappropriate content"),
                new("Details", "This player listing needs admin review.")
            ]));
        Assert.Equal(HttpStatusCode.Redirect, playerReportResponse.StatusCode);
        Assert.Equal(playerPath, playerReportResponse.Headers.Location?.ToString());

        var opportunityPath = $"/opportunities/{opportunityId}";
        var opportunityPage = await client.GetAsync(opportunityPath);
        Assert.Equal(HttpStatusCode.OK, opportunityPage.StatusCode);
        var opportunityHtml = await opportunityPage.Content.ReadAsStringAsync();
        Assert.Contains("Report listing", opportunityHtml);
        Assert.Contains("Submit report", opportunityHtml);
        var opportunityReportResponse = await client.PostAsync(
            $"/opportunities/{opportunityId}/report",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", ExtractAntiForgeryToken(opportunityHtml, opportunityPath)),
                new("Reason", "Misleading listing"),
                new("Details", "This opportunity needs admin review.")
            ]));
        Assert.Equal(HttpStatusCode.Redirect, opportunityReportResponse.StatusCode);
        Assert.Equal(opportunityPath, opportunityReportResponse.Headers.Location?.ToString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var reports = await dbContext.ListingReports
            .Where(report => report.ReporterUserId == reporter.Id)
            .OrderBy(report => report.CreatedAt)
            .ToArrayAsync();

        Assert.Equal(2, reports.Length);
        Assert.Contains(reports, report => report.PlayerListingId == playerListingId
            && report.Reason == "Inappropriate content"
            && report.Status == TryOutSpotListingReportStatuses.Pending);
        Assert.Contains(reports, report => report.OpportunityId == opportunityId
            && report.Reason == "Misleading listing"
            && report.Status == TryOutSpotListingReportStatuses.Pending);
    }

    private static async Task LoginWebUserAsync(HttpClient client, string email)
    {
        var loginPage = await client.GetAsync("/account/login");
        loginPage.EnsureSuccessStatusCode();
        var loginHtml = await loginPage.Content.ReadAsStringAsync();
        var loginResponse = await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", ExtractAntiForgeryToken(loginHtml, "/account/login")),
                new("Email", email),
                new("Password", "Tryout2026"),
                new("RememberMe", "true")
            ]));

        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);
    }

    private static string ExtractAntiForgeryToken(string html, string path)
    {
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);

        return match.Success
            ? match.Groups[1].Value
            : throw new InvalidOperationException($"No anti-forgery token was found on {path}.");
    }

    private static Guid GetActiveSportId(TryOutSpotWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return dbContext.Sports
            .Where(sport => sport.IsActive && sport.Name == "Softball")
            .Select(sport => sport.Id)
            .Single();
    }

    private static Guid SeedPlayerProfile(TryOutSpotWebApplicationFactory factory, Guid userId, Guid sportId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var player = new Player
        {
            Id = Guid.NewGuid(),
            FirstName = "Jordan",
            LastName = "Taylor",
            DateOfBirth = DateTime.UtcNow.AddYears(-14),
            City = "Oklahoma City",
            State = "OK",
            ZipCode = "73102",
            ContactVisibility = "Public",
            IsSearchable = true,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };

        dbContext.Players.Add(player);
        dbContext.PlayerSports.Add(new PlayerSport
        {
            Id = Guid.NewGuid(),
            PlayerId = player.Id,
            SportId = sportId,
            PrimaryPosition = "Catcher",
            IsActive = true,
            CreatedAt = now
        });
        dbContext.UserPlayerRelationships.Add(new UserPlayerRelationship
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PlayerId = player.Id,
            Relationship = "ParentGuardian",
            CanManage = true,
            CreatedAt = now
        });
        dbContext.SaveChanges();
        return player.Id;
    }

    private static Guid SeedPlayerListing(
        TryOutSpotWebApplicationFactory factory,
        Guid userId,
        Guid sportId,
        Guid playerId,
        string title)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var listing = new PlayerListing
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PlayerId = playerId,
            SportId = sportId,
            ListingType = TryOutSpotPlayerListingTypes.PickupPlayer,
            Title = title,
            Description = "Seeded listing for report page tests.",
            City = "Oklahoma City",
            State = "OK",
            ZipCode = "73102",
            IsPublished = true,
            IsSearchable = true,
            PublishedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };

        dbContext.PlayerListings.Add(listing);
        dbContext.SaveChanges();
        return listing.Id;
    }

    private static (Guid TeamId, Guid OpportunityId) SeedTeamOpportunity(
        TryOutSpotWebApplicationFactory factory,
        Guid userId,
        Guid sportId,
        string teamName,
        string opportunityTitle)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var team = new Team
        {
            Id = Guid.NewGuid(),
            Name = teamName,
            TeamLevel = "14U",
            GeographicScope = "Regional",
            Description = "Seeded team for report page tests.",
            City = "Wichita",
            State = "KS",
            ZipCode = "67202",
            IsSearchable = true,
            IsContactInfoVisible = true,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };
        var opportunity = new Opportunity
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            SportId = sportId,
            Type = "tryout",
            Title = opportunityTitle,
            Description = "Seeded opportunity for report page tests.",
            City = "Wichita",
            State = "KS",
            ZipCode = "67202",
            IsPublished = true,
            PublishedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };

        dbContext.Teams.Add(team);
        dbContext.TeamSports.Add(new TeamSport
        {
            Id = Guid.NewGuid(),
            TeamId = team.Id,
            SportId = sportId,
            CompetitionLevel = "A",
            AgeGroup = "14U",
            TravelLevel = "Regional",
            IsActive = true,
            CreatedAt = now
        });
        dbContext.UserTeamRoles.Add(new UserTeamRole
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TeamId = team.Id,
            Role = "Owner",
            StartDate = now,
            IsActive = true,
            CreatedAt = now
        });
        dbContext.Opportunities.Add(opportunity);
        dbContext.SaveChanges();
        return (team.Id, opportunity.Id);
    }
}
