using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Listings;
using TryOutSpot.Web.Models.Account;
using TryOutSpot.Web.Models.Listings;
using TryOutSpot.Web.Models.Moderation;
using TryOutSpot.Web.Models.UserManagement;

namespace TryOutSpot.Web.Tests;

public sealed class ListingReportsApiTests
{
    [Fact]
    public async Task Users_CanReportPlayerListingsAndTeamOpportunities()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var playerOwner = await factory.CreateUserAsync("report-player-owner@example.com", [TryOutSpotRoles.Parent]);
        var teamOwner = await factory.CreateUserAsync("report-team-owner@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var teamReporter = await factory.CreateUserAsync("report-team-rep@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var parentReporter = await factory.CreateUserAsync("report-parent@example.com", [TryOutSpotRoles.Parent]);
        var sportId = GetActiveSportId(factory);
        var playerListingId = SeedPlayerListing(factory, playerOwner.Id, sportId, "Suspicious pickup player listing");
        var (_, opportunityId) = SeedTeamOpportunity(factory, teamOwner.Id, sportId, "Reportable team", "Suspicious team tryout");

        var teamClient = await CreateAuthorizedClientAsync(factory, teamReporter.Email!);
        var playerReportResponse = await teamClient.PostAsJsonAsync(
            $"/api/player-listings/{playerListingId}/report",
            new ReportListingRequest
            {
                Reason = "Inappropriate content",
                Details = "The listing includes wording that should be reviewed."
            });

        Assert.Equal(HttpStatusCode.Created, playerReportResponse.StatusCode);
        var playerReport = await playerReportResponse.Content.ReadFromJsonAsync<ListingReportActionResponse>();
        Assert.NotNull(playerReport);
        Assert.Equal(TryOutSpotListingReportTargetTypes.PlayerListing, playerReport.Report.TargetType);
        Assert.Equal(TryOutSpotListingReportStatuses.Pending, playerReport.Report.Status);

        var duplicateResponse = await teamClient.PostAsJsonAsync(
            $"/api/player-listings/{playerListingId}/report",
            new ReportListingRequest
            {
                Reason = "Inappropriate content"
            });

        Assert.Equal(HttpStatusCode.OK, duplicateResponse.StatusCode);

        var parentClient = await CreateAuthorizedClientAsync(factory, parentReporter.Email!);
        var opportunityReportResponse = await parentClient.PostAsJsonAsync(
            $"/api/team-listings/opportunities/{opportunityId}/report",
            new ReportListingRequest
            {
                Reason = "Misleading listing"
            });

        Assert.Equal(HttpStatusCode.Created, opportunityReportResponse.StatusCode);
        var opportunityReport = await opportunityReportResponse.Content.ReadFromJsonAsync<ListingReportActionResponse>();
        Assert.NotNull(opportunityReport);
        Assert.Equal(TryOutSpotListingReportTargetTypes.TeamOpportunity, opportunityReport.Report.TargetType);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, await dbContext.ListingReports.CountAsync());
    }

    [Fact]
    public async Task PlatformAdmin_CanListAndReviewReportedListings()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var playerOwner = await factory.CreateUserAsync("admin-report-player-owner@example.com", [TryOutSpotRoles.Parent]);
        var teamOwner = await factory.CreateUserAsync("admin-report-team-owner@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var reporter = await factory.CreateUserAsync("admin-report-reporter@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var sportId = GetActiveSportId(factory);
        var playerListingId = SeedPlayerListing(factory, playerOwner.Id, sportId, "Needs admin player review");
        var (_, opportunityId) = SeedTeamOpportunity(factory, teamOwner.Id, sportId, "Admin Review Aces", "Needs admin team review");
        var playerReportId = SeedListingReport(factory, reporter.Id, playerListingId: playerListingId);
        SeedListingReport(factory, reporter.Id, opportunityId: opportunityId);
        var adminClient = await CreateAdminClientAsync(factory);

        var reportsResponse = await adminClient.GetAsync("/api/admin/listings/reports?status=Pending&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, reportsResponse.StatusCode);
        var reports = await reportsResponse.Content.ReadFromJsonAsync<AdminListingReportListResponse>();
        Assert.NotNull(reports);
        Assert.Equal(2, reports.TotalCount);

        var playerListingsResponse = await adminClient.GetAsync("/api/admin/listings/player-listings?q=Needs%20admin");
        Assert.Equal(HttpStatusCode.OK, playerListingsResponse.StatusCode);
        var playerListings = await playerListingsResponse.Content.ReadFromJsonAsync<AdminListingListResponse>();
        Assert.NotNull(playerListings);
        var playerListing = Assert.Single(playerListings.Listings);
        Assert.Equal(playerListingId, playerListing.TargetId);
        Assert.Equal(1, playerListing.OpenReportCount);

        var opportunitiesResponse = await adminClient.GetAsync("/api/admin/listings/team-opportunities?q=team%20review");
        Assert.Equal(HttpStatusCode.OK, opportunitiesResponse.StatusCode);
        var opportunities = await opportunitiesResponse.Content.ReadFromJsonAsync<AdminListingListResponse>();
        Assert.NotNull(opportunities);
        var opportunity = Assert.Single(opportunities.Listings);
        Assert.Equal(opportunityId, opportunity.TargetId);
        Assert.Equal(1, opportunity.OpenReportCount);

        var reviewResponse = await adminClient.PostAsJsonAsync(
            $"/api/admin/listings/reports/{playerReportId}/review",
            new ReviewListingReportRequest
            {
                Status = TryOutSpotListingReportStatuses.ActionTaken,
                AdminNotes = "Unpublished listing and contacted owner."
            });

        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
        var reviewed = await reviewResponse.Content.ReadFromJsonAsync<AdminListingReportDetailResponse>();
        Assert.NotNull(reviewed);
        Assert.Equal(TryOutSpotListingReportStatuses.ActionTaken, reviewed.Report.Status);
        Assert.NotNull(reviewed.Report.ReviewedAtUtc);
        Assert.NotNull(reviewed.PlayerListing);
        Assert.Equal(0, reviewed.PlayerListing.OpenReportCount);
    }

    [Fact]
    public async Task PlatformAdmin_CanViewUserWithProfilesListingsAndTeams()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("admin-profile-user@example.com", [TryOutSpotRoles.Parent, TryOutSpotRoles.TeamRepresentative]);
        var reporter = await factory.CreateUserAsync("admin-profile-reporter@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var sportId = GetActiveSportId(factory);
        SeedPlayerProfile(factory, user.Id, sportId);
        var playerListingId = SeedPlayerListing(factory, user.Id, sportId, "Profile listing with report");
        SeedListingReport(factory, reporter.Id, playerListingId: playerListingId);
        var (teamId, opportunityId) = SeedTeamOpportunity(factory, user.Id, sportId, "Profile Admin Team", "Profile admin tryout");
        SeedListingReport(factory, reporter.Id, opportunityId: opportunityId);
        var adminClient = await CreateAdminClientAsync(factory);

        var response = await adminClient.GetAsync($"/api/user-management/users/{user.Id}/profile");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.Content.ReadFromJsonAsync<ManagedUserProfileResponse>();
        Assert.NotNull(profile);
        Assert.Equal(user.Id, profile.User.UserId);
        Assert.Single(profile.PlayerProfiles);
        var listing = Assert.Single(profile.PlayerListings);
        Assert.Equal(1, listing.OpenReportCount);
        var team = Assert.Single(profile.Teams);
        Assert.Equal(teamId, team.TeamId);
        var opportunity = Assert.Single(profile.TeamOpportunities);
        Assert.Equal(opportunityId, opportunity.OpportunityId);
        Assert.Equal(1, opportunity.OpenReportCount);
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

    private static Guid SeedPlayerListing(
        TryOutSpotWebApplicationFactory factory,
        Guid userId,
        Guid sportId,
        string title)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var listing = new PlayerListing
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            SportId = sportId,
            ListingType = TryOutSpotPlayerListingTypes.PickupPlayer,
            Title = title,
            Description = "Seeded listing for report workflow tests.",
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

    private static Guid SeedPlayerProfile(
        TryOutSpotWebApplicationFactory factory,
        Guid userId,
        Guid sportId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var player = new Player
        {
            Id = Guid.NewGuid(),
            FirstName = "Casey",
            LastName = "Morgan",
            DateOfBirth = DateTime.UtcNow.AddYears(-13),
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
            PrimaryPosition = "Pitcher",
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
            GeographicScope = "Local",
            City = "Oklahoma City",
            State = "OK",
            ZipCode = "73102",
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
            Description = "Seeded opportunity for report workflow tests.",
            RegistrationRequired = false,
            RegistrationFee = 0m,
            EventDate = now.AddDays(10),
            City = "Oklahoma City",
            State = "OK",
            ZipCode = "73102",
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
            IsActive = true,
            CreatedAt = now
        });
        dbContext.UserTeamRoles.Add(new UserTeamRole
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TeamId = team.Id,
            Role = TryOutSpotRoles.TeamRepresentative,
            StartDate = now,
            IsActive = true,
            CreatedAt = now
        });
        dbContext.Opportunities.Add(opportunity);
        dbContext.SaveChanges();
        return (team.Id, opportunity.Id);
    }

    private static Guid SeedListingReport(
        TryOutSpotWebApplicationFactory factory,
        Guid reporterUserId,
        Guid? playerListingId = null,
        Guid? opportunityId = null)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var report = new ListingReport
        {
            Id = Guid.NewGuid(),
            ReporterUserId = reporterUserId,
            PlayerListingId = playerListingId,
            OpportunityId = opportunityId,
            Reason = "Inappropriate content",
            Details = "Seeded moderation report.",
            Status = TryOutSpotListingReportStatuses.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.ListingReports.Add(report);
        dbContext.SaveChanges();
        return report.Id;
    }

    private static async Task<HttpClient> CreateAdminClientAsync(TryOutSpotWebApplicationFactory factory)
    {
        var adminTokens = await factory.LoginAsPlatformAdminAsync();
        var client = factory.CreateClient();
        Authorize(client, adminTokens);
        return client;
    }

    private static async Task<HttpClient> CreateAuthorizedClientAsync(TryOutSpotWebApplicationFactory factory, string email)
    {
        var client = factory.CreateClient();
        var tokens = await LoginAsync(client, email);
        Authorize(client, tokens);
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
