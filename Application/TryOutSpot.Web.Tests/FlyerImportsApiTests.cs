using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Listings;
using TryOutSpot.Web.Models.Account;
using TryOutSpot.Web.Models.Admin;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Tests;

public sealed class FlyerImportsApiTests
{
    [Theory]
    [InlineData("adding players", "roster_opening")]
    [InlineData("guest players", "pickup_player")]
    [InlineData("tournament", "tournament")]
    [InlineData("other", "other")]
    public async Task PlatformAdmin_CanCreateDraftOpportunityFromFlyerImport(string flyerType, string expectedOpportunityType)
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var adminTokens = await factory.LoginAsPlatformAdminAsync();
        var client = factory.CreateClient();
        Authorize(client, adminTokens);
        var sportId = GetActiveSportId(factory, "Softball");
        var teamName = $"Flyer API {expectedOpportunityType} {Guid.NewGuid():N}";

        var createResponse = await client.PostAsJsonAsync(
            "/api/admin/flyer-imports",
            new CreateFlyerImportRequest
            {
                SourcePlatform = "facebook",
                SourceUrl = "https://www.facebook.com/example/posts/flyer",
                OriginalExternalImageUrl = "https://example.test/flyer.jpg",
                SportId = sportId,
                SportName = "Softball",
                OpportunityType = flyerType,
                Title = $"{teamName} flyer",
                TeamName = teamName,
                AgeGroup = "14U",
                EventDate = DateTime.UtcNow.AddDays(14),
                Address = "123 Ballpark Ave",
                City = "Wichita",
                State = "KS",
                ZipCode = "67202",
                ContactEmail = "coach@example.test",
                ContactPhone = "555-555-2026",
                WebsiteUrl = "https://example.test/register",
                Description = "Imported from a flyer for admin review.",
                ExtractedJson = """{"team":"Flyer API"}"""
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createResult = await createResponse.Content.ReadFromJsonAsync<FlyerImportActionResponse>();
        Assert.NotNull(createResult);

        var createListingResponse = await client.PostAsJsonAsync(
            $"/api/admin/flyer-imports/{createResult.FlyerImport.ImportId}/create-listing",
            new CreateListingFromFlyerImportRequest
            {
                PublishImmediately = false
            });

        Assert.Equal(HttpStatusCode.OK, createListingResponse.StatusCode);
        var createListingResult = await createListingResponse.Content.ReadFromJsonAsync<FlyerImportActionResponse>();
        Assert.NotNull(createListingResult);
        Assert.NotNull(createListingResult.TeamId);
        Assert.NotNull(createListingResult.OpportunityId);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var flyerImport = await dbContext.FlyerImports.SingleAsync(currentImport => currentImport.Id == createResult.FlyerImport.ImportId);
        var opportunity = await dbContext.Opportunities
            .Include(currentOpportunity => currentOpportunity.Team)
            .SingleAsync(currentOpportunity => currentOpportunity.Id == createListingResult.OpportunityId);

        Assert.Equal(TryOutSpotFlyerImportStatuses.DraftCreated, flyerImport.Status);
        Assert.Equal(expectedOpportunityType, opportunity.Type);
        Assert.Equal(teamName, opportunity.Team.Name);
        Assert.Equal("14U", opportunity.AgeGroup);
        Assert.False(opportunity.IsPublished);
        Assert.True(opportunity.IsActive);
        Assert.Equal("67202", opportunity.ZipCode);
        Assert.True(await dbContext.TeamSports.AnyAsync(teamSport =>
            teamSport.TeamId == opportunity.TeamId
            && teamSport.SportId == sportId
            && teamSport.IsActive));
    }

    [Fact]
    public async Task PlatformAdmin_CreateFlyerImport_EnrichesVenueLocationFromPlaceSearch()
    {
        await using var factory = new TryOutSpotWebApplicationFactory(services =>
        {
            services.RemoveAll<IFlyerPlaceSearchClient>();
            services.AddScoped<IFlyerPlaceSearchClient, TestFlyerPlaceSearchClient>();
        });
        var adminTokens = await factory.LoginAsPlatformAdminAsync();
        var client = factory.CreateClient();
        Authorize(client, adminTokens);
        var sportId = GetActiveSportId(factory, "Softball");
        var teamName = $"Venue Enrichment Eagles {Guid.NewGuid():N}";

        var createResponse = await client.PostAsJsonAsync(
            "/api/admin/flyer-imports",
            new CreateFlyerImportRequest
            {
                SourcePlatform = "facebook",
                OriginalExternalImageUrl = "https://example.test/eagles-flyer.jpg",
                SportId = sportId,
                SportName = "Softball",
                OpportunityType = "tryout",
                Title = $"{teamName} tryouts",
                TeamName = teamName,
                AgeGroup = "12U",
                EventDate = DateTime.UtcNow.AddDays(30),
                Location = "Capital Federal Sports Complex Liberty Field",
                ContactEmail = "coach@example.test"
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createResult = await createResponse.Content.ReadFromJsonAsync<FlyerImportActionResponse>();
        Assert.NotNull(createResult);
        Assert.Equal("Capital Federal Sports Complex Liberty Field", createResult.FlyerImport.Location);
        Assert.Equal("2200 Old 210 Hwy", createResult.FlyerImport.Address);
        Assert.Equal("Liberty", createResult.FlyerImport.City);
        Assert.Equal("MO", createResult.FlyerImport.State);
        Assert.Equal("64068", createResult.FlyerImport.ZipCode);

        var createListingResponse = await client.PostAsJsonAsync(
            $"/api/admin/flyer-imports/{createResult.FlyerImport.ImportId}/create-listing",
            new CreateListingFromFlyerImportRequest
            {
                PublishImmediately = false
            });

        Assert.Equal(HttpStatusCode.OK, createListingResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var opportunity = await dbContext.Opportunities
            .SingleAsync(currentOpportunity => currentOpportunity.Team.Name == teamName);

        Assert.Equal("Capital Federal Sports Complex Liberty Field", opportunity.Location);
        Assert.Equal("2200 Old 210 Hwy", opportunity.Address);
        Assert.Equal("Liberty", opportunity.City);
        Assert.Equal("MO", opportunity.State);
        Assert.Equal("64068", opportunity.ZipCode);
    }

    [Fact]
    public async Task PlatformAdmin_CreateFlyerImport_UsesFirstCityZipWhenOnlyCityAndStateAreKnown()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        SeedZipCode(factory, "64063", "Lee's Summit", "MO");
        SeedZipCode(factory, "64064", "Lees Summit", "MO");
        var adminTokens = await factory.LoginAsPlatformAdminAsync();
        var client = factory.CreateClient();
        Authorize(client, adminTokens);
        var sportId = GetActiveSportId(factory, "Softball");
        var teamName = $"City Zip Reign {Guid.NewGuid():N}";

        var createResponse = await client.PostAsJsonAsync(
            "/api/admin/flyer-imports",
            new CreateFlyerImportRequest
            {
                SourcePlatform = "facebook",
                OriginalExternalImageUrl = "https://example.test/reign-flyer.jpg",
                SportId = sportId,
                SportName = "Softball",
                OpportunityType = "tryout",
                Title = $"{teamName} tryouts",
                TeamName = teamName,
                AgeGroup = "10U",
                EventDate = DateTime.UtcNow.AddDays(30),
                City = "Lee’s Summit",
                State = "Missouri",
                ContactPhone = "816-555-2026"
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var createResult = await createResponse.Content.ReadFromJsonAsync<FlyerImportActionResponse>();
        Assert.NotNull(createResult);
        Assert.Equal("Lee’s Summit", createResult.FlyerImport.City);
        Assert.Equal("MO", createResult.FlyerImport.State);
        Assert.Equal("64063", createResult.FlyerImport.ZipCode);
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

    private static void Authorize(HttpClient client, AuthTokenResponse tokenResponse)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            tokenResponse.TokenType,
            tokenResponse.AccessToken);
    }

    private static void SeedZipCode(
        TryOutSpotWebApplicationFactory factory,
        string zipCode,
        string city,
        string state)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.ZipCodeGeographies.Add(new ZipCodeGeography
        {
            ZipCode = zipCode,
            City = city,
            State = state,
            Latitude = 39.01m,
            Longitude = -94.35m,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        dbContext.SaveChanges();
    }

    private sealed class TestFlyerPlaceSearchClient : IFlyerPlaceSearchClient
    {
        public Task<FlyerPlaceSearchResult?> SearchAsync(
            string query,
            CancellationToken cancellationToken)
        {
            FlyerPlaceSearchResult? result = query.Contains("Capital Federal Sports Complex", StringComparison.OrdinalIgnoreCase)
                ? new FlyerPlaceSearchResult(
                    "Capital Federal Sports Complex of Liberty",
                    "2200 Old 210 Hwy",
                    "Liberty",
                    "MO",
                    "64068")
                : null;

            return Task.FromResult(result);
        }
    }
}
