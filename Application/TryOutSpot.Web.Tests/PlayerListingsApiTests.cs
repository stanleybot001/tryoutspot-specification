using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Listings;
using TryOutSpot.Web.Models.Account;
using TryOutSpot.Web.Models.Listings;

namespace TryOutSpot.Web.Tests;

public sealed class PlayerListingsApiTests
{
    [Fact]
    public async Task ParentUser_CanCreateAndSearchListing()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("listing-parent@example.com", [TryOutSpotRoles.Parent]);
        var sportId = GetActiveSportId(factory, "Softball");
        var authorizedClient = await CreateAuthorizedClientAsync(factory, user.Email!);

        var createResponse = await authorizedClient.PostAsJsonAsync(
            "/api/player-listings",
            new CreatePlayerListingRequest
            {
                ListingType = TryOutSpotPlayerListingTypes.PickupPlayer,
                Title = "Guest pitcher available for weekend events",
                Description = "Looking for 12U-14U weekend tournament pickup opportunities.",
                SportId = sportId,
                City = "McPherson",
                State = "KS",
                ZipCode = "67460",
                IsPublished = true,
                IsSearchable = true
            });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var action = await createResponse.Content.ReadFromJsonAsync<PlayerListingActionResponse>();
        Assert.NotNull(action);
        Assert.Equal(TryOutSpotPlayerListingTypes.PickupPlayer, action.Listing.ListingType);

        var mineResponse = await authorizedClient.GetAsync("/api/player-listings/mine");
        Assert.Equal(HttpStatusCode.OK, mineResponse.StatusCode);
        var mineListings = await mineResponse.Content.ReadFromJsonAsync<PlayerListingListResponse>();
        Assert.NotNull(mineListings);
        Assert.Single(mineListings.Listings);

        var publicClient = factory.CreateClient();
        var searchResponse = await publicClient.GetAsync(
            "/api/player-listings/search?listingType=pickup_player&q=pitcher&zipCode=67460");

        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        var searchListings = await searchResponse.Content.ReadFromJsonAsync<PlayerListingListResponse>();
        Assert.NotNull(searchListings);
        Assert.Single(searchListings.Listings);
    }

    [Fact]
    public async Task CoachWithoutPlayerRole_CannotCreatePlayerListing()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("listing-coach@example.com", [TryOutSpotRoles.Coach]);
        var authorizedClient = await CreateAuthorizedClientAsync(factory, user.Email!);

        var response = await authorizedClient.PostAsJsonAsync(
            "/api/player-listings",
            new CreatePlayerListingRequest
            {
                ListingType = TryOutSpotPlayerListingTypes.LookingForTeam,
                Title = "Looking for spring roster home",
                Description = "Multi-position player seeking a new team.",
                City = "Wichita",
                State = "KS",
                ZipCode = "67202"
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Search_HidesUnpublishedListings()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("listing-draft@example.com", [TryOutSpotRoles.Parent]);
        var authorizedClient = await CreateAuthorizedClientAsync(factory, user.Email!);

        var createResponse = await authorizedClient.PostAsJsonAsync(
            "/api/player-listings",
            new CreatePlayerListingRequest
            {
                ListingType = TryOutSpotPlayerListingTypes.UsedEquipment,
                Title = "Used catcher gear set",
                Description = "Quality used set, includes chest protector and shin guards.",
                City = "McPherson",
                State = "KS",
                ZipCode = "67460",
                IsPublished = false,
                IsSearchable = true
            });
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var publicClient = factory.CreateClient();
        var searchResponse = await publicClient.GetAsync("/api/player-listings/search?q=catcher");
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);

        var listings = await searchResponse.Content.ReadFromJsonAsync<PlayerListingListResponse>();
        Assert.NotNull(listings);
        Assert.Empty(listings.Listings);
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
}
