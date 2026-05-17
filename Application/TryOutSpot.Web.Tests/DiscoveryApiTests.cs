using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Models.Discovery;

namespace TryOutSpot.Web.Tests;

public sealed class DiscoveryApiTests
{
    [Fact]
    public async Task SearchEndpoints_ByZipRadius_ReturnNearbyTeamsOrganizationsAndOpportunities()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        SeedDiscoveryData(factory);
        var client = factory.CreateClient();

        var teamsResponse = await client.GetAsync("/api/discovery/teams?originZipCode=67460&radiusMiles=75");
        Assert.Equal(HttpStatusCode.OK, teamsResponse.StatusCode);
        var teams = await teamsResponse.Content.ReadFromJsonAsync<TeamDiscoveryListResponse>();
        Assert.NotNull(teams);
        Assert.Equal("67460", teams.SearchOriginZipCode);
        Assert.Equal(75, teams.SearchRadiusMiles);
        Assert.Single(teams.Teams);
        Assert.Equal("McPherson Aces", teams.Teams.Single().Name);
        Assert.NotNull(teams.Teams.Single().DistanceMiles);

        var organizationsResponse = await client.GetAsync("/api/discovery/organizations?originZipCode=67460&radiusMiles=75");
        Assert.Equal(HttpStatusCode.OK, organizationsResponse.StatusCode);
        var organizations = await organizationsResponse.Content.ReadFromJsonAsync<OrganizationDiscoveryListResponse>();
        Assert.NotNull(organizations);
        Assert.Single(organizations.Organizations);
        Assert.Equal("Central Kansas Baseball Club", organizations.Organizations.Single().Name);
        Assert.NotNull(organizations.Organizations.Single().DistanceMiles);

        var opportunitiesResponse = await client.GetAsync("/api/discovery/opportunities?originZipCode=67460&radiusMiles=75");
        Assert.Equal(HttpStatusCode.OK, opportunitiesResponse.StatusCode);
        var opportunities = await opportunitiesResponse.Content.ReadFromJsonAsync<OpportunityDiscoveryListResponse>();
        Assert.NotNull(opportunities);
        Assert.Single(opportunities.Opportunities);
        Assert.Equal("14U Open Tryout", opportunities.Opportunities.Single().Title);
        Assert.NotNull(opportunities.Opportunities.Single().DistanceMiles);
        Assert.False(opportunities.AdvancedFiltersApplied);
    }

    [Fact]
    public async Task SearchTeams_WithUnknownOriginZip_ReturnsBadRequest()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/discovery/teams?originZipCode=99999&radiusMiles=50");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static void SeedDiscoveryData(TryOutSpotWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        dbContext.ZipCodeGeographies.AddRange(
            new ZipCodeGeography
            {
                ZipCode = "67460",
                City = "McPherson",
                State = "KS",
                Latitude = 38.3700m,
                Longitude = -97.6642m,
                IsActive = true
            },
            new ZipCodeGeography
            {
                ZipCode = "67202",
                City = "Wichita",
                State = "KS",
                Latitude = 37.6872m,
                Longitude = -97.3301m,
                IsActive = true
            },
            new ZipCodeGeography
            {
                ZipCode = "73102",
                City = "Oklahoma City",
                State = "OK",
                Latitude = 35.4676m,
                Longitude = -97.5164m,
                IsActive = true
            });

        var softballSportId = dbContext.Sports
            .Where(sport => sport.IsActive && sport.Name == "Softball")
            .Select(sport => sport.Id)
            .Single();

        var now = DateTime.UtcNow;
        var localOrganization = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Central Kansas Baseball Club",
            City = "McPherson",
            State = "KS",
            ZipCode = "67460",
            IsSearchable = true,
            IsContactInfoVisible = true,
            IsAcademy = false,
            IsVerified = true,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };
        var farOrganization = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Metro Oklahoma Fastpitch",
            City = "Oklahoma City",
            State = "OK",
            ZipCode = "73102",
            IsSearchable = true,
            IsContactInfoVisible = true,
            IsAcademy = false,
            IsVerified = false,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };

        dbContext.Organizations.AddRange(localOrganization, farOrganization);

        var localTeam = new Team
        {
            Id = Guid.NewGuid(),
            OrganizationId = localOrganization.Id,
            Name = "McPherson Aces",
            TeamLevel = "14U",
            GeographicScope = "Local",
            City = "McPherson",
            State = "KS",
            ZipCode = "67460",
            IsSearchable = true,
            IsContactInfoVisible = true,
            IsElite = false,
            IsVerified = true,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };
        var farTeam = new Team
        {
            Id = Guid.NewGuid(),
            OrganizationId = farOrganization.Id,
            Name = "OKC Storm",
            TeamLevel = "14U",
            GeographicScope = "Regional",
            City = "Oklahoma City",
            State = "OK",
            ZipCode = "73102",
            IsSearchable = true,
            IsContactInfoVisible = true,
            IsElite = false,
            IsVerified = false,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };

        dbContext.Teams.AddRange(localTeam, farTeam);

        dbContext.TeamSports.AddRange(
            new TeamSport
            {
                Id = Guid.NewGuid(),
                TeamId = localTeam.Id,
                SportId = softballSportId,
                IsActive = true,
                CreatedAt = now
            },
            new TeamSport
            {
                Id = Guid.NewGuid(),
                TeamId = farTeam.Id,
                SportId = softballSportId,
                IsActive = true,
                CreatedAt = now
            });

        dbContext.Opportunities.AddRange(
            new Opportunity
            {
                Id = Guid.NewGuid(),
                TeamId = localTeam.Id,
                SportId = softballSportId,
                Type = "tryout",
                Title = "14U Open Tryout",
                RegistrationRequired = true,
                RegistrationFee = 25m,
                EventDate = now.AddDays(10),
                City = "McPherson",
                State = "KS",
                ZipCode = "67460",
                IsPublished = true,
                PublishedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                IsActive = true
            },
            new Opportunity
            {
                Id = Guid.NewGuid(),
                TeamId = farTeam.Id,
                SportId = softballSportId,
                Type = "tryout",
                Title = "OKC Weekly Session",
                RegistrationRequired = true,
                RegistrationFee = 20m,
                EventDate = now.AddDays(12),
                City = "Oklahoma City",
                State = "OK",
                ZipCode = "73102",
                IsPublished = true,
                PublishedAt = now,
                CreatedAt = now,
                UpdatedAt = now,
                IsActive = true
            });

        dbContext.SaveChanges();
    }
}
