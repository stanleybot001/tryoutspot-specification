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

public sealed class FavoriteDashboardPageTests
{
    [Fact]
    public async Task OpportunityFavoritePost_WithWebCookie_DoesNotRequireAntiforgeryToken()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var viewer = await factory.CreateUserAsync("favorites-no-token@example.com", [TryOutSpotRoles.Parent]);
        var opportunityId = SeedOpportunity(factory);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, viewer.Email!);

        var response = await client.PostAsync($"/opportunities/{opportunityId}/favorite", content: null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/opportunities/{opportunityId}", response.Headers.Location?.ToString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var favoriteExists = await dbContext.UserFavorites
            .AnyAsync(favorite => favorite.UserId == viewer.Id && favorite.OpportunityId == opportunityId);
        Assert.True(favoriteExists);
    }

    [Theory]
    [InlineData(TryOutSpotRoles.Parent, "parent-favorites-tile@example.com")]
    [InlineData(TryOutSpotRoles.TeamRepresentative, "team-favorites-tile@example.com")]
    public async Task Onboarding_ShowsFavoritesTileForDashboardRoles(string role, string email)
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var viewer = await factory.CreateUserAsync(email, [role]);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, viewer.Email!);

        var onboardingResponse = await client.GetAsync("/account/onboarding");
        Assert.Equal(HttpStatusCode.OK, onboardingResponse.StatusCode);
        var html = await onboardingResponse.Content.ReadAsStringAsync();

        Assert.Contains("My Favorites", html);
        Assert.Contains("/account/onboarding/favorites", html);
    }

    [Fact]
    public async Task FavoritesPage_ShowsTeamOpportunityFavoriteAndCanRemoveIt()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var viewer = await factory.CreateUserAsync(
            "team-favorites-dashboard@example.com",
            [TryOutSpotRoles.TeamRepresentative]);
        var seeded = SeedFavorites(factory, viewer.Id);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, viewer.Email!);

        var onboardingResponse = await client.GetAsync("/account/onboarding");
        Assert.Equal(HttpStatusCode.OK, onboardingResponse.StatusCode);
        var html = await onboardingResponse.Content.ReadAsStringAsync();

        Assert.Contains("My Favorites", html);
        Assert.Contains("/account/onboarding/favorites", html);
        Assert.DoesNotContain("Dashboard favorite player listing", html);
        Assert.DoesNotContain("Dashboard favorite opportunity", html);

        var favoritesResponse = await client.GetAsync("/account/onboarding/favorites");
        Assert.Equal(HttpStatusCode.OK, favoritesResponse.StatusCode);
        var favoritesHtml = await favoritesResponse.Content.ReadAsStringAsync();

        Assert.Contains("Dashboard favorite player listing", favoritesHtml);
        Assert.Contains("Dashboard favorite opportunity", favoritesHtml);
        Assert.Contains($"/player-listings/{seeded.PlayerListingId}", favoritesHtml);
        Assert.Contains($"/opportunities/{seeded.OpportunityId}", favoritesHtml);

        var removeResponse = await client.PostAsync(
            $"/opportunities/{seeded.OpportunityId}/favorite/remove",
            new FormUrlEncodedContent(
            [
                new("returnUrl", "/account/onboarding/favorites")
            ]));

        Assert.Equal(HttpStatusCode.Redirect, removeResponse.StatusCode);
        Assert.Equal("/account/onboarding/favorites", removeResponse.Headers.Location?.ToString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var opportunityFavoriteExists = await dbContext.UserFavorites
            .AnyAsync(favorite => favorite.UserId == viewer.Id && favorite.OpportunityId == seeded.OpportunityId);
        Assert.False(opportunityFavoriteExists);
    }

    private static (Guid PlayerListingId, Guid OpportunityId) SeedFavorites(
        TryOutSpotWebApplicationFactory factory,
        Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var sportId = dbContext.Sports
            .Where(sport => sport.IsActive && sport.Name == "Softball")
            .Select(sport => sport.Id)
            .Single();

        var playerListingId = Guid.NewGuid();
        dbContext.PlayerListings.Add(new PlayerListing
        {
            Id = playerListingId,
            UserId = userId,
            SportId = sportId,
            ListingType = TryOutSpotPlayerListingTypes.PickupPlayer,
            Title = "Dashboard favorite player listing",
            Description = "Saved from player discovery.",
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

        var teamId = Guid.NewGuid();
        dbContext.Teams.Add(new Team
        {
            Id = teamId,
            Name = "Dashboard Favorite Team",
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
            Title = "Dashboard favorite opportunity",
            RegistrationRequired = true,
            RegistrationFee = 15m,
            EventDate = now.AddDays(12),
            City = "McPherson",
            State = "KS",
            ZipCode = "67460",
            IsPublished = true,
            PublishedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        });

        dbContext.UserFavorites.AddRange(
            new UserFavorite
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                PlayerListingId = playerListingId,
                CreatedAt = now
            },
            new UserFavorite
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                OpportunityId = opportunityId,
                CreatedAt = now
            });
        dbContext.SaveChanges();

        return (playerListingId, opportunityId);
    }

    private static Guid SeedOpportunity(TryOutSpotWebApplicationFactory factory)
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
            Name = "Favorite Token Team",
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
            Title = "No token favorite opportunity",
            RegistrationRequired = true,
            RegistrationFee = 10m,
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
        dbContext.SaveChanges();

        return opportunityId;
    }

    private static async Task LoginWebUserAsync(HttpClient client, string email)
    {
        var loginPage = await client.GetAsync("/account/login");
        loginPage.EnsureSuccessStatusCode();
        var antiForgeryToken = ReadAntiForgeryToken(await loginPage.Content.ReadAsStringAsync());
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

    private static string ReadAntiForgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);

        return match.Success
            ? match.Groups[1].Value
            : throw new InvalidOperationException("No anti-forgery token was found.");
    }
}
