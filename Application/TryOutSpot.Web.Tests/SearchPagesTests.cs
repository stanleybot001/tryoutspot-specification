using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Listings;

namespace TryOutSpot.Web.Tests;

public sealed class SearchPagesTests
{
    [Fact]
    public async Task SearchTeamItems_FreeParent_ConstrainsAllTypesToTryouts()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var parent = await factory.CreateUserAsync("free-parent-team-search@example.com", [TryOutSpotRoles.Parent]);
        SeedTeamItemSearchData(factory);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, parent.Email!);

        var response = await client.GetAsync("/account/search/team-items?type=all");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Free Player/Parent search is limited to tryouts", html);
        Assert.Contains("Free parent visible tryout", html);
        Assert.DoesNotContain("Premium only tournament", html);
    }

    [Fact]
    public async Task SearchTeamItems_PremiumParent_CanSearchAllOpportunityTypes()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var parent = await factory.CreateUserAsync("premium-parent-team-search@example.com", [TryOutSpotRoles.Parent]);
        await AddSubscriptionAsync(factory, parent.Id, TryOutSpotPlanCodes.PremiumPlayer, "active");
        SeedTeamItemSearchData(factory);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, parent.Email!);

        var response = await client.GetAsync("/account/search/team-items?type=all");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Free parent visible tryout", html);
        Assert.Contains("Premium only tournament", html);
    }

    [Fact]
    public async Task SearchPlayers_TeamUser_OrdersPriorityPlayerListingsFirst()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var regularOwner = await factory.CreateUserAsync("regular-player-listing-owner@example.com", [TryOutSpotRoles.Parent]);
        var priorityOwner = await factory.CreateUserAsync("priority-player-listing-owner@example.com", [TryOutSpotRoles.Parent]);
        var teamUser = await factory.CreateUserAsync("team-player-search@example.com", [TryOutSpotRoles.TeamRepresentative]);
        await AddSubscriptionAsync(factory, priorityOwner.Id, TryOutSpotPlanCodes.PremiumPlayer, "active");
        SeedPlayerSearchListings(factory, regularOwner.Id, priorityOwner.Id);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, teamUser.Email!);

        var response = await client.GetAsync("/account/search/players");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var priorityIndex = html.IndexOf("Priority pitcher available", StringComparison.Ordinal);
        var regularIndex = html.IndexOf("Regular catcher available", StringComparison.Ordinal);
        Assert.True(priorityIndex >= 0);
        Assert.True(regularIndex >= 0);
        Assert.True(priorityIndex < regularIndex);
    }

    [Fact]
    public async Task SearchPlayers_TeamUser_HonorsLinkedPlayerSearchAndContactVisibility()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var owner = await factory.CreateUserAsync("player-visibility-listing-owner@example.com", [TryOutSpotRoles.Parent]);
        var teamUser = await factory.CreateUserAsync("team-player-visibility-search@example.com", [TryOutSpotRoles.TeamRepresentative]);
        SeedPlayerVisibilitySearchListings(factory, owner.Id);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, teamUser.Email!);

        var response = await client.GetAsync("/account/search/players?listingType=pickup_player");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Public searchable player", html);
        Assert.Contains("Coach-only searchable player", html);
        Assert.DoesNotContain("Hidden profile player", html);
        Assert.DoesNotContain("Unlinked parent listing", html);
        Assert.Contains("Birthday:", html);
        Assert.Contains("Apr 5, 2011", html);
        Assert.Contains("Public Academy", html);
        Assert.Contains("Searchable Club", html);
        Assert.Contains("Grad year:", html);
        Assert.Contains("5'8\" / 145 lb", html);
        Assert.Contains("Throws:", html);
        Assert.Contains("Bats:", html);
        Assert.DoesNotContain("Sport not set", html);
        Assert.DoesNotContain("Not linked", html);
        Assert.DoesNotContain("Not set", html);
    }

    [Fact]
    public async Task SearchTeamItems_WithSmallRadius_SuggestsFartherRadiusMatches()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var parent = await factory.CreateUserAsync("radius-suggestion-parent@example.com", [TryOutSpotRoles.Parent]);
        SeedZipCodeGeographies(factory);
        SeedRadiusSuggestionOpportunity(factory);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, parent.Email!);

        var response = await client.GetAsync("/account/search/team-items?originZipCode=67460&radiusMiles=30&type=tryout");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("No team items matched these filters.", html);
        Assert.Contains("Within 60 miles", html);
        Assert.Contains("Wichita radius suggestion tryout", html);
    }

    [Fact]
    public async Task PublicSharePages_RenderShareControlsOnlyForListingOwnersAndSocialMetadata()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var owner = await factory.CreateUserAsync(
            "public-share-owner@example.com",
            [TryOutSpotRoles.Parent, TryOutSpotRoles.TeamRepresentative]);
        var seeded = SeedPublicSharePages(factory, owner.Id);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var opportunityResponse = await client.GetAsync($"/opportunities/{seeded.OpportunityId}");

        Assert.Equal(HttpStatusCode.OK, opportunityResponse.StatusCode);
        var opportunityHtml = WebUtility.HtmlDecode(await opportunityResponse.Content.ReadAsStringAsync());
        Assert.DoesNotContain("Share this opportunity", opportunityHtml);
        Assert.DoesNotContain("Copy post", opportunityHtml);
        Assert.Contains($"http://localhost/opportunities/{seeded.OpportunityId}", opportunityHtml);
        Assert.Contains("property=\"og:title\"", opportunityHtml);

        var listingResponse = await client.GetAsync($"/player-listings/{seeded.ListingId}");

        Assert.Equal(HttpStatusCode.OK, listingResponse.StatusCode);
        var listingHtml = WebUtility.HtmlDecode(await listingResponse.Content.ReadAsStringAsync());
        Assert.DoesNotContain("Share this listing", listingHtml);
        Assert.DoesNotContain("Copy post", listingHtml);
        Assert.Contains($"http://localhost/player-listings/{seeded.ListingId}", listingHtml);
        Assert.Contains("View player profile", listingHtml);
        Assert.Contains("property=\"og:title\"", listingHtml);

        await LoginWebUserAsync(client, owner.Email!);

        var ownerOpportunityResponse = await client.GetAsync($"/opportunities/{seeded.OpportunityId}");

        Assert.Equal(HttpStatusCode.OK, ownerOpportunityResponse.StatusCode);
        var ownerOpportunityHtml = WebUtility.HtmlDecode(await ownerOpportunityResponse.Content.ReadAsStringAsync());
        Assert.Contains("Share this opportunity", ownerOpportunityHtml);
        Assert.Contains("Copy post", ownerOpportunityHtml);
        Assert.Contains($"http://localhost/opportunities/{seeded.OpportunityId}", ownerOpportunityHtml);

        var ownerListingResponse = await client.GetAsync($"/player-listings/{seeded.ListingId}");

        Assert.Equal(HttpStatusCode.OK, ownerListingResponse.StatusCode);
        var ownerListingHtml = WebUtility.HtmlDecode(await ownerListingResponse.Content.ReadAsStringAsync());
        Assert.Contains("Share this listing", ownerListingHtml);
        Assert.Contains("Copy post", ownerListingHtml);
        Assert.Contains($"http://localhost/player-listings/{seeded.ListingId}", ownerListingHtml);
    }

    [Fact]
    public async Task PublicPlayerProfilePage_HidesShareControlsAndCoachOnlyContactForAnonymous()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var owner = await factory.CreateUserAsync("public-profile-owner@example.com", [TryOutSpotRoles.Parent]);
        var playerId = SeedPublicPlayerProfile(factory, owner.Id);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.GetAsync($"/players/{playerId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("Share this profile", html);
        Assert.DoesNotContain("Copy post", html);
        Assert.DoesNotContain("Public profile link", html);
        Assert.Contains("Shareable Profile", html);
        Assert.Contains("Public profile listing", html);
        Assert.Contains("Contact details are limited", html);
        Assert.DoesNotContain("555-777-0000", html);

        await LoginWebUserAsync(client, owner.Email!);
        var manageResponse = await client.GetAsync("/account/onboarding/player-profiles");

        Assert.Equal(HttpStatusCode.OK, manageResponse.StatusCode);
        var manageHtml = WebUtility.HtmlDecode(await manageResponse.Content.ReadAsStringAsync());
        Assert.Contains("View public profile", manageHtml);
        Assert.Contains($"http://localhost/players/{playerId}", manageHtml);
    }

    [Fact]
    public async Task PublicPlayerProfilePage_PremiumProfileMovesSocialAndListingSections()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var owner = await factory.CreateUserAsync("premium-public-profile-owner@example.com", [TryOutSpotRoles.Parent]);
        await AddSubscriptionAsync(factory, owner.Id, TryOutSpotPlanCodes.PremiumPlayer, "active");
        var playerId = SeedPublicPlayerProfile(factory, owner.Id);
        var client = factory.CreateClient();

        var response = await client.GetAsync($"/players/{playerId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        var socialIndex = html.IndexOf("profile-top-links", StringComparison.Ordinal);
        var summaryIndex = html.IndexOf("<span>Birthday</span>", StringComparison.Ordinal);
        var metricsIndex = html.IndexOf("Performance metrics", StringComparison.Ordinal);
        var activeListingsIndex = html.IndexOf("Active listings", StringComparison.Ordinal);
        var sportsIndex = html.IndexOf("Sports and positions", StringComparison.Ordinal);

        Assert.NotEqual(-1, socialIndex);
        Assert.NotEqual(-1, summaryIndex);
        Assert.NotEqual(-1, metricsIndex);
        Assert.NotEqual(-1, activeListingsIndex);
        Assert.NotEqual(-1, sportsIndex);
        Assert.True(socialIndex < summaryIndex);
        Assert.True(metricsIndex < activeListingsIndex);
        Assert.True(activeListingsIndex < sportsIndex);
        Assert.Contains("profile-link-card-facebook", html);
        Assert.Contains("profile-link-card-x", html);
        Assert.Contains("profile-link-card-instagram", html);
        Assert.Contains("<svg viewBox=\"0 0 24 24\"", html);
    }

    private static void SeedTeamItemSearchData(TryOutSpotWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var sportId = dbContext.Sports
            .Where(sport => sport.IsActive && sport.Name == "Softball")
            .Select(sport => sport.Id)
            .Single();
        var team = CreateTeam("Search Page Team", "McPherson", "KS", "67460", now);
        dbContext.Teams.Add(team);
        dbContext.Opportunities.AddRange(
            CreateOpportunity(team.Id, sportId, "tryout", "Free parent visible tryout", "McPherson", "KS", "67460", now),
            CreateOpportunity(team.Id, sportId, "tournament", "Premium only tournament", "McPherson", "KS", "67460", now));
        dbContext.SaveChanges();
    }

    private static void SeedPlayerSearchListings(
        TryOutSpotWebApplicationFactory factory,
        Guid regularOwnerId,
        Guid priorityOwnerId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var sportId = dbContext.Sports
            .Where(sport => sport.IsActive && sport.Name == "Softball")
            .Select(sport => sport.Id)
            .Single();
        var regularPlayer = CreatePlayer("Regular", "Catcher", true, "VerifiedCoachesOnly", now);
        var priorityPlayer = CreatePlayer("Priority", "Pitcher", true, "VerifiedCoachesOnly", now);

        dbContext.Players.AddRange(regularPlayer, priorityPlayer);
        dbContext.PlayerListings.AddRange(
            new PlayerListing
            {
                Id = Guid.NewGuid(),
                UserId = regularOwnerId,
                PlayerId = regularPlayer.Id,
                SportId = sportId,
                ListingType = TryOutSpotPlayerListingTypes.PickupPlayer,
                Title = "Regular catcher available",
                Description = "Catcher available for weekend tournament pickup.",
                City = "McPherson",
                State = "KS",
                ZipCode = "67460",
                IsPublished = true,
                IsSearchable = true,
                PublishedAt = now.AddMinutes(5),
                CreatedAt = now,
                UpdatedAt = now.AddMinutes(5),
                IsActive = true
            },
            new PlayerListing
            {
                Id = Guid.NewGuid(),
                UserId = priorityOwnerId,
                PlayerId = priorityPlayer.Id,
                SportId = sportId,
                ListingType = TryOutSpotPlayerListingTypes.PickupPlayer,
                Title = "Priority pitcher available",
                Description = "Pitcher available for weekend tournament pickup.",
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
    }

    private static void SeedPlayerVisibilitySearchListings(
        TryOutSpotWebApplicationFactory factory,
        Guid ownerId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var sportId = dbContext.Sports
            .Where(sport => sport.IsActive && sport.Name == "Softball")
            .Select(sport => sport.Id)
            .Single();
        var publicPlayer = CreatePlayer("Public", "Searchable", true, "Public", now);
        var coachOnlyPlayer = CreatePlayer("Coach", "Only", true, "VerifiedCoachesOnly", now);
        var hiddenPlayer = CreatePlayer("Hidden", "Profile", false, "Public", now);

        dbContext.Players.AddRange(publicPlayer, coachOnlyPlayer, hiddenPlayer);
        dbContext.PlayerListings.AddRange(
            CreatePlayerListing(ownerId, publicPlayer.Id, sportId, "Public searchable player", now),
            CreatePlayerListing(ownerId, coachOnlyPlayer.Id, sportId, "Coach-only searchable player", now),
            CreatePlayerListing(ownerId, hiddenPlayer.Id, sportId, "Hidden profile player", now),
            new PlayerListing
            {
                Id = Guid.NewGuid(),
                UserId = ownerId,
                SportId = sportId,
                ListingType = TryOutSpotPlayerListingTypes.PickupPlayer,
                Title = "Unlinked parent listing",
                Description = "This parent-owned listing is not tied to a player profile.",
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
    }

    private static void SeedZipCodeGeographies(TryOutSpotWebApplicationFactory factory)
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
            });
        dbContext.SaveChanges();
    }

    private static void SeedRadiusSuggestionOpportunity(TryOutSpotWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var sportId = dbContext.Sports
            .Where(sport => sport.IsActive && sport.Name == "Softball")
            .Select(sport => sport.Id)
            .Single();
        var team = CreateTeam("Wichita Radius Team", "Wichita", "KS", "67202", now);
        dbContext.Teams.Add(team);
        dbContext.Opportunities.Add(CreateOpportunity(
            team.Id,
            sportId,
            "tryout",
            "Wichita radius suggestion tryout",
            "Wichita",
            "KS",
            "67202",
            now));
        dbContext.SaveChanges();
    }

    private static (Guid OpportunityId, Guid ListingId) SeedPublicSharePages(
        TryOutSpotWebApplicationFactory factory,
        Guid ownerId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var sportId = dbContext.Sports
            .Where(sport => sport.IsActive && sport.Name == "Softball")
            .Select(sport => sport.Id)
            .Single();

        var team = CreateTeam("Public Share Team", "McPherson", "KS", "67460", now);
        var player = CreatePlayer("Shareable", "Profile", true, "Public", now);
        var opportunity = CreateOpportunity(
            team.Id,
            sportId,
            "tryout",
            "Public share tryout",
            "McPherson",
            "KS",
            "67460",
            now);
        var listing = CreatePlayerListing(ownerId, player.Id, sportId, "Public share player listing", now);

        dbContext.Teams.Add(team);
        dbContext.UserTeamRoles.Add(new UserTeamRole
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            TeamId = team.Id,
            Role = TryOutSpotRoles.TeamRepresentative,
            IsActive = true,
            CreatedAt = now
        });
        dbContext.Players.Add(player);
        dbContext.PlayerSports.Add(CreatePlayerSport(player.Id, sportId, now));
        dbContext.PlayerListings.Add(listing);
        dbContext.Opportunities.Add(opportunity);
        dbContext.SaveChanges();

        return (opportunity.Id, listing.Id);
    }

    private static Guid SeedPublicPlayerProfile(
        TryOutSpotWebApplicationFactory factory,
        Guid ownerId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var sportId = dbContext.Sports
            .Where(sport => sport.IsActive && sport.Name == "Softball")
            .Select(sport => sport.Id)
            .Single();
        var player = CreatePlayer("Shareable", "Profile", true, "VerifiedCoachesOnly", now);
        player.ContactPhone = "555-777-0000";
        player.ContactEmail = "shareable.profile@example.com";
        player.ExitVelocity = "78 mph";
        player.ThrowingVelocity = "63 mph";
        player.SocialMediaLinks = """
            {"facebook":"https://facebook.com/shareable.profile","x":"https://x.com/shareableprofile","instagram":"https://instagram.com/shareable.profile"}
            """;

        dbContext.Players.Add(player);
        dbContext.PlayerSports.Add(CreatePlayerSport(player.Id, sportId, now));
        dbContext.UserPlayerRelationships.Add(new UserPlayerRelationship
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            PlayerId = player.Id,
            Relationship = "Parent",
            CanManage = true,
            CreatedAt = now
        });
        dbContext.PlayerListings.Add(CreatePlayerListing(ownerId, player.Id, sportId, "Public profile listing", now));
        dbContext.SaveChanges();

        return player.Id;
    }

    private static Team CreateTeam(
        string name,
        string city,
        string state,
        string zipCode,
        DateTime now)
    {
        return new Team
        {
            Id = Guid.NewGuid(),
            Name = name,
            TeamLevel = "14U",
            GeographicScope = "Local",
            City = city,
            State = state,
            ZipCode = zipCode,
            IsSearchable = true,
            IsContactInfoVisible = true,
            IsElite = false,
            IsVerified = false,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };
    }

    private static Player CreatePlayer(
        string firstName,
        string lastName,
        bool isSearchable,
        string contactVisibility,
        DateTime now)
    {
        return new Player
        {
            Id = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName,
            DateOfBirth = new DateTime(2011, 4, 5, 0, 0, 0, DateTimeKind.Utc),
            City = "McPherson",
            State = "KS",
            ZipCode = "67460",
            SchoolName = $"{firstName} Academy",
            CurrentTeamName = $"{lastName} Club",
            GraduationYear = 2029,
            Height = "5'8\"",
            Weight = "145 lb",
            ThrowsHand = "Right",
            BatsHand = "Left",
            ContactVisibility = contactVisibility,
            IsSearchable = isSearchable,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };
    }

    private static PlayerSport CreatePlayerSport(Guid playerId, Guid sportId, DateTime now)
    {
        return new PlayerSport
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            SportId = sportId,
            SkillLevel = "Competitive",
            PrimaryPosition = "Pitcher",
            SecondaryPositions = "First base",
            ExperienceLevel = "Travel",
            YearsPlaying = 5,
            Availability = "Weekends",
            IsActive = true,
            CreatedAt = now
        };
    }

    private static PlayerListing CreatePlayerListing(
        Guid ownerId,
        Guid playerId,
        Guid sportId,
        string title,
        DateTime now)
    {
        return new PlayerListing
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            PlayerId = playerId,
            SportId = sportId,
            ListingType = TryOutSpotPlayerListingTypes.PickupPlayer,
            Title = title,
            Description = "Visibility test player listing.",
            City = "McPherson",
            State = "KS",
            ZipCode = "67460",
            IsPublished = true,
            IsSearchable = true,
            PublishedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };
    }

    private static Opportunity CreateOpportunity(
        Guid teamId,
        Guid sportId,
        string type,
        string title,
        string city,
        string state,
        string zipCode,
        DateTime now)
    {
        return new Opportunity
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            SportId = sportId,
            Type = type,
            Title = title,
            RegistrationRequired = true,
            RegistrationFee = 20m,
            EventDate = now.AddDays(10),
            City = city,
            State = state,
            ZipCode = zipCode,
            IsPublished = true,
            PublishedAt = now,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };
    }

    private static async Task AddSubscriptionAsync(
        TryOutSpotWebApplicationFactory factory,
        Guid userId,
        string planCode,
        string status,
        string billingInterval = BillingIntervalCodes.Month)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        dbContext.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PlanType = planCode,
            Status = status,
            ScopeType = TryOutSpotSubscriptionScopeTypes.Account,
            ScopeId = null,
            StripeCustomerId = $"cus_{Guid.NewGuid():N}",
            StripeSubscriptionId = $"sub_{Guid.NewGuid():N}",
            CurrentPeriodStart = now,
            CurrentPeriodEnd = billingInterval == BillingIntervalCodes.Year ? now.AddYears(1) : now.AddMonths(1),
            Amount = 29m,
            Currency = "USD",
            BillingInterval = billingInterval,
            CreatedAt = now,
            UpdatedAt = now
        });

        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();
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
