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
    public async Task Onboarding_ShowsFavoritesAndCanRemoveOpportunityFavorite()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var viewer = await factory.CreateUserAsync(
            "favorites-dashboard@example.com",
            [TryOutSpotRoles.Parent, TryOutSpotRoles.TeamRepresentative]);
        var seeded = SeedFavorites(factory, viewer.Id);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, viewer.Email!);

        var onboardingResponse = await client.GetAsync("/account/onboarding");
        Assert.Equal(HttpStatusCode.OK, onboardingResponse.StatusCode);
        var html = await onboardingResponse.Content.ReadAsStringAsync();

        Assert.Contains("My favorites", html);
        Assert.Contains("Dashboard favorite player listing", html);
        Assert.Contains("Dashboard favorite opportunity", html);
        Assert.Contains($"/player-listings/{seeded.PlayerListingId}", html);
        Assert.Contains($"/opportunities/{seeded.OpportunityId}", html);

        var antiForgeryToken = ReadAntiForgeryToken(html);
        var removeResponse = await client.PostAsync(
            $"/opportunities/{seeded.OpportunityId}/favorite/remove",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", antiForgeryToken),
                new("returnUrl", "/account/onboarding")
            ]));

        Assert.Equal(HttpStatusCode.Redirect, removeResponse.StatusCode);
        Assert.Equal("/account/onboarding", removeResponse.Headers.Location?.ToString());

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
