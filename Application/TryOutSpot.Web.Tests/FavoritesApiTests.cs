using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Listings;
using TryOutSpot.Web.Models.Account;
using TryOutSpot.Web.Models.Discovery;
using TryOutSpot.Web.Models.Favorites;
using TryOutSpot.Web.Models.Listings;

namespace TryOutSpot.Web.Tests;

public sealed class FavoritesApiTests
{
    [Fact]
    public async Task TeamUser_CanFavoriteAndRemovePlayerListing()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var listingOwner = await factory.CreateUserAsync("favorite-listing-owner@example.com", [TryOutSpotRoles.Parent]);
        var teamUser = await factory.CreateUserAsync("favorite-team@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var listingId = SeedPublishedPlayerListing(factory, listingOwner.Id, "Favorite pickup catcher");
        var client = await CreateAuthorizedClientAsync(factory, teamUser.Email!);

        var addResponse = await client.PostAsync($"/api/favorites/player-listings/{listingId}", content: null);

        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
        var addPayload = await addResponse.Content.ReadFromJsonAsync<FavoriteActionResponse>();
        Assert.NotNull(addPayload);
        Assert.True(addPayload.IsFavorited);

        var mineResponse = await client.GetAsync("/api/favorites/mine");
        Assert.Equal(HttpStatusCode.OK, mineResponse.StatusCode);
        var minePayload = await mineResponse.Content.ReadFromJsonAsync<FavoriteListResponse>();
        Assert.NotNull(minePayload);
        var favorite = Assert.Single(minePayload.PlayerListings);
        Assert.Equal(listingId, favorite.ListingId);
        Assert.Equal("Favorite pickup catcher", favorite.Title);

        var searchResponse = await client.GetAsync("/api/player-listings/search?q=catcher");
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        var searchPayload = await searchResponse.Content.ReadFromJsonAsync<PlayerListingListResponse>();
        Assert.NotNull(searchPayload);
        Assert.True(Assert.Single(searchPayload.Listings).IsFavorited);

        var removeResponse = await client.PostAsync($"/api/favorites/player-listings/{listingId}/remove", content: null);

        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);
        var removePayload = await removeResponse.Content.ReadFromJsonAsync<FavoriteActionResponse>();
        Assert.NotNull(removePayload);
        Assert.False(removePayload.IsFavorited);

        var searchAfterRemoveResponse = await client.GetAsync("/api/player-listings/search?q=catcher");
        Assert.Equal(HttpStatusCode.OK, searchAfterRemoveResponse.StatusCode);
        var searchAfterRemovePayload = await searchAfterRemoveResponse.Content.ReadFromJsonAsync<PlayerListingListResponse>();
        Assert.NotNull(searchAfterRemovePayload);
        Assert.False(Assert.Single(searchAfterRemovePayload.Listings).IsFavorited);
    }

    [Fact]
    public async Task ParentUser_CanFavoriteAndRemoveOpportunity()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var parentUser = await factory.CreateUserAsync("favorite-parent@example.com", [TryOutSpotRoles.Parent]);
        var opportunityId = SeedPublishedOpportunity(factory, "Favorite 14U Tryout");
        var client = await CreateAuthorizedClientAsync(factory, parentUser.Email!);

        var addResponse = await client.PostAsync($"/api/favorites/opportunities/{opportunityId}", content: null);

        Assert.Equal(HttpStatusCode.OK, addResponse.StatusCode);
        var addPayload = await addResponse.Content.ReadFromJsonAsync<FavoriteActionResponse>();
        Assert.NotNull(addPayload);
        Assert.True(addPayload.IsFavorited);

        var mineResponse = await client.GetAsync("/api/favorites/mine");
        Assert.Equal(HttpStatusCode.OK, mineResponse.StatusCode);
        var minePayload = await mineResponse.Content.ReadFromJsonAsync<FavoriteListResponse>();
        Assert.NotNull(minePayload);
        var favorite = Assert.Single(minePayload.Opportunities);
        Assert.Equal(opportunityId, favorite.OpportunityId);
        Assert.Equal("Favorite 14U Tryout", favorite.Title);

        var searchResponse = await client.GetAsync("/api/discovery/opportunities?q=Favorite");
        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        var searchPayload = await searchResponse.Content.ReadFromJsonAsync<OpportunityDiscoveryListResponse>();
        Assert.NotNull(searchPayload);
        Assert.True(Assert.Single(searchPayload.Opportunities).IsFavorited);

        var removeResponse = await client.PostAsync($"/api/favorites/opportunities/{opportunityId}/remove", content: null);

        Assert.Equal(HttpStatusCode.OK, removeResponse.StatusCode);
        var removePayload = await removeResponse.Content.ReadFromJsonAsync<FavoriteActionResponse>();
        Assert.NotNull(removePayload);
        Assert.False(removePayload.IsFavorited);

        var searchAfterRemoveResponse = await client.GetAsync("/api/discovery/opportunities?q=Favorite");
        Assert.Equal(HttpStatusCode.OK, searchAfterRemoveResponse.StatusCode);
        var searchAfterRemovePayload = await searchAfterRemoveResponse.Content.ReadFromJsonAsync<OpportunityDiscoveryListResponse>();
        Assert.NotNull(searchAfterRemovePayload);
        Assert.False(Assert.Single(searchAfterRemovePayload.Opportunities).IsFavorited);
    }

    private static Guid SeedPublishedPlayerListing(
        TryOutSpotWebApplicationFactory factory,
        Guid ownerUserId,
        string title)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var sportId = dbContext.Sports
            .Where(sport => sport.IsActive && sport.Name == "Softball")
            .Select(sport => sport.Id)
            .Single();

        var listingId = Guid.NewGuid();
        var playerId = Guid.NewGuid();
        dbContext.Players.Add(new Player
        {
            Id = playerId,
            FirstName = "Favorite",
            LastName = "Catcher",
            DateOfBirth = now.AddYears(-14).Date,
            City = "McPherson",
            State = "KS",
            ZipCode = "67460",
            ContactVisibility = "VerifiedCoachesOnly",
            IsSearchable = true,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        });
        dbContext.PlayerListings.Add(new PlayerListing
        {
            Id = listingId,
            UserId = ownerUserId,
            PlayerId = playerId,
            SportId = sportId,
            ListingType = TryOutSpotPlayerListingTypes.PickupPlayer,
            Title = title,
            Description = "Catcher available for tournament pickup work.",
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
        dbContext.SaveChanges();

        return listingId;
    }

    private static Guid SeedPublishedOpportunity(TryOutSpotWebApplicationFactory factory, string title)
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
            Name = "Favorites Test Team",
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

        var opportunityId = Guid.NewGuid();
        dbContext.Opportunities.Add(new Opportunity
        {
            Id = opportunityId,
            TeamId = teamId,
            SportId = sportId,
            Type = "tryout",
            Title = title,
            RegistrationRequired = true,
            RegistrationFee = 25m,
            EventDate = now.AddDays(14),
            City = "McPherson",
            State = "KS",
            ZipCode = "67460",
            IsPublished = true,
            PublishedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        });
        dbContext.SaveChanges();

        return opportunityId;
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
