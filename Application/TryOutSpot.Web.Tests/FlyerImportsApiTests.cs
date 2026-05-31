using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Listings;
using TryOutSpot.Web.Models.Account;
using TryOutSpot.Web.Models.Admin;

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
}
