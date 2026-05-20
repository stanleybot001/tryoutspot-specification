using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Listings;
using TryOutSpot.Web.Models.Account;
using TryOutSpot.Web.Models.Listings;

namespace TryOutSpot.Web.Tests;

public sealed class PlayerListingsApiTests
{
    [Fact]
    public async Task TeamBasicSearch_ReturnsAdvancedFiltersAppliedMetadata()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var teamUser = await factory.CreateUserAsync("listing-team-basic-meta@example.com", [TryOutSpotRoles.TeamRepresentative]);
        await AddSubscriptionAsync(factory, teamUser.Id, TryOutSpotPlanCodes.TeamBasic, "active");
        var teamClient = await CreateAuthorizedClientAsync(factory, teamUser.Email!);

        var response = await teamClient.GetAsync("/api/player-listings/search");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<PlayerListingListResponse>();
        Assert.NotNull(payload);
        Assert.True(payload.AdvancedFiltersApplied);
    }

    [Fact]
    public async Task FreeCoachSearch_WithSkillLevelFilter_ReturnsBadRequest()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var coachUser = await factory.CreateUserAsync("listing-free-coach-skill@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var coachClient = await CreateAuthorizedClientAsync(factory, coachUser.Email!);

        var response = await coachClient.GetAsync("/api/player-listings/search?skillLevel=advanced");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ParentUser_CanCreateAndSearchListing()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("listing-parent@example.com", [TryOutSpotRoles.Parent]);
        var sportId = GetActiveSportId(factory, "Softball");
        var playerId = SeedManagedPlayer(factory, user.Id);
        var authorizedClient = await CreateAuthorizedClientAsync(factory, user.Email!);

        var createResponse = await authorizedClient.PostAsJsonAsync(
            "/api/player-listings",
            new CreatePlayerListingRequest
            {
                ListingType = TryOutSpotPlayerListingTypes.PickupPlayer,
                PlayerId = playerId,
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
    public async Task Search_DoesNotReturnListingsWithoutPlayerProfile()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("listing-unlinked-parent@example.com", [TryOutSpotRoles.Parent]);
        SeedUnlinkedSearchablePlayerListing(factory, user.Id, "Parent account gear listing");

        var publicClient = factory.CreateClient();
        var searchResponse = await publicClient.GetAsync("/api/player-listings/search?q=parent");

        Assert.Equal(HttpStatusCode.OK, searchResponse.StatusCode);
        var searchListings = await searchResponse.Content.ReadFromJsonAsync<PlayerListingListResponse>();
        Assert.NotNull(searchListings);
        Assert.Empty(searchListings.Listings);
    }

    [Fact]
    public async Task Create_SearchableListingWithoutPlayerProfile_ReturnsBadRequest()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("listing-unlinked-searchable-create@example.com", [TryOutSpotRoles.Parent]);
        var authorizedClient = await CreateAuthorizedClientAsync(factory, user.Email!);

        var response = await authorizedClient.PostAsJsonAsync(
            "/api/player-listings",
            new CreatePlayerListingRequest
            {
                ListingType = TryOutSpotPlayerListingTypes.UsedEquipment,
                Title = "Unlinked searchable listing",
                Description = "Should require a player before search visibility.",
                City = "McPherson",
                State = "KS",
                ZipCode = "67460",
                IsPublished = true,
                IsSearchable = true
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CoachWithoutPlayerRole_CannotCreatePlayerListing()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("listing-coach@example.com", [TryOutSpotRoles.TeamRepresentative]);
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
        var sportId = GetActiveSportId(factory, "Softball");
        var playerId = SeedManagedPlayer(factory, user.Id);
        var authorizedClient = await CreateAuthorizedClientAsync(factory, user.Email!);

        var createResponse = await authorizedClient.PostAsJsonAsync(
            "/api/player-listings",
            new CreatePlayerListingRequest
            {
                ListingType = TryOutSpotPlayerListingTypes.PickupPlayer,
                PlayerId = playerId,
                SportId = sportId,
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

    [Fact]
    public async Task Search_HonorsLinkedPlayerProfileSearchAndContactVisibility()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var owner = await factory.CreateUserAsync("listing-profile-visibility-owner@example.com", [TryOutSpotRoles.Parent]);
        var teamUser = await factory.CreateUserAsync("listing-profile-visibility-team@example.com", [TryOutSpotRoles.TeamRepresentative]);
        SeedPlayerProfileVisibilityListings(factory, owner.Id);

        var publicClient = factory.CreateClient();
        var publicResponse = await publicClient.GetAsync("/api/player-listings/search?listingType=pickup_player");

        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);
        var publicListings = await publicResponse.Content.ReadFromJsonAsync<PlayerListingListResponse>();
        Assert.NotNull(publicListings);
        Assert.Contains(publicListings.Listings, listing => listing.Title == "Public profile listing");
        Assert.DoesNotContain(publicListings.Listings, listing => listing.Title == "Coach-only profile listing");
        Assert.DoesNotContain(publicListings.Listings, listing => listing.Title == "Search-disabled profile listing");

        var teamClient = await CreateAuthorizedClientAsync(factory, teamUser.Email!);
        var teamResponse = await teamClient.GetAsync("/api/player-listings/search?listingType=pickup_player");

        Assert.Equal(HttpStatusCode.OK, teamResponse.StatusCode);
        var teamListings = await teamResponse.Content.ReadFromJsonAsync<PlayerListingListResponse>();
        Assert.NotNull(teamListings);
        Assert.Contains(teamListings.Listings, listing => listing.Title == "Public profile listing");
        Assert.Contains(teamListings.Listings, listing => listing.Title == "Coach-only profile listing");
        Assert.DoesNotContain(teamListings.Listings, listing => listing.Title == "Search-disabled profile listing");
    }

    [Fact]
    public async Task Search_ByOriginZipAndRadius_FiltersAndIncludesDistance()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        SeedZipCodeGeographies(factory,
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

        var user = await factory.CreateUserAsync("listing-radius@example.com", [TryOutSpotRoles.Parent]);
        var sportId = GetActiveSportId(factory, "Softball");
        var localPlayerId = SeedManagedPlayer(factory, user.Id, firstName: "Local", lastName: "Catcher", city: "McPherson", state: "KS", zipCode: "67460");
        var distantPlayerId = SeedManagedPlayer(factory, user.Id, firstName: "Far", lastName: "Infielder", city: "Oklahoma City", state: "OK", zipCode: "73102");
        var authorizedClient = await CreateAuthorizedClientAsync(factory, user.Email!);

        var localCreateResponse = await authorizedClient.PostAsJsonAsync(
            "/api/player-listings",
            new CreatePlayerListingRequest
            {
                ListingType = TryOutSpotPlayerListingTypes.PickupPlayer,
                PlayerId = localPlayerId,
                SportId = sportId,
                Title = "Local catcher available",
                Description = "Within the metro area.",
                City = "McPherson",
                State = "KS",
                ZipCode = "67460",
                IsPublished = true,
                IsSearchable = true
            });
        Assert.Equal(HttpStatusCode.Created, localCreateResponse.StatusCode);

        var distantCreateResponse = await authorizedClient.PostAsJsonAsync(
            "/api/player-listings",
            new CreatePlayerListingRequest
            {
                ListingType = TryOutSpotPlayerListingTypes.PickupPlayer,
                PlayerId = distantPlayerId,
                SportId = sportId,
                Title = "Far away infielder available",
                Description = "Outside the radius test range.",
                City = "Oklahoma City",
                State = "OK",
                ZipCode = "73102",
                IsPublished = true,
                IsSearchable = true
            });
        Assert.Equal(HttpStatusCode.Created, distantCreateResponse.StatusCode);

        var publicClient = factory.CreateClient();
        var response = await publicClient.GetAsync(
            "/api/player-listings/search?originZipCode=67460&radiusMiles=75&listingType=pickup_player");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var searchResult = await response.Content.ReadFromJsonAsync<PlayerListingListResponse>();
        Assert.NotNull(searchResult);
        Assert.Equal("67460", searchResult.SearchOriginZipCode);
        Assert.Equal(75, searchResult.SearchRadiusMiles);
        Assert.Single(searchResult.Listings);
        Assert.NotNull(searchResult.Listings.First().DistanceMiles);
    }

    [Fact]
    public async Task Search_WithUnknownOriginZip_ReturnsBadRequest()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var publicClient = factory.CreateClient();

        var response = await publicClient.GetAsync("/api/player-listings/search?originZipCode=99999&radiusMiles=50");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
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

    private static void SeedPlayerProfileVisibilityListings(
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
        var publicPlayer = CreatePlayer("Public", "Profile", true, "Public", now);
        var coachOnlyPlayer = CreatePlayer("Coach", "Profile", true, "VerifiedCoachesOnly", now);
        var searchDisabledPlayer = CreatePlayer("Hidden", "Profile", false, "Public", now);

        dbContext.Players.AddRange(publicPlayer, coachOnlyPlayer, searchDisabledPlayer);
        dbContext.PlayerListings.AddRange(
            CreatePlayerListing(ownerId, publicPlayer.Id, sportId, "Public profile listing", now),
            CreatePlayerListing(ownerId, coachOnlyPlayer.Id, sportId, "Coach-only profile listing", now),
            CreatePlayerListing(ownerId, searchDisabledPlayer.Id, sportId, "Search-disabled profile listing", now));
        dbContext.SaveChanges();
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
            DateOfBirth = now.AddYears(-15).Date,
            City = "McPherson",
            State = "KS",
            ZipCode = "67460",
            ContactVisibility = contactVisibility,
            IsSearchable = isSearchable,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
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
            Description = "Player profile visibility test listing.",
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

    private static Guid SeedManagedPlayer(
        TryOutSpotWebApplicationFactory factory,
        Guid ownerId,
        string firstName = "Alex",
        string lastName = "Rivera",
        string city = "McPherson",
        string state = "KS",
        string zipCode = "67460")
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var player = new Player
        {
            Id = Guid.NewGuid(),
            FirstName = firstName,
            LastName = lastName,
            DateOfBirth = now.AddYears(-14).Date,
            City = city,
            State = state,
            ZipCode = zipCode,
            ContactVisibility = "Public",
            IsSearchable = true,
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };

        dbContext.Players.Add(player);
        dbContext.UserPlayerRelationships.Add(new UserPlayerRelationship
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            PlayerId = player.Id,
            Relationship = "Parent",
            CanManage = true,
            CreatedAt = now
        });
        dbContext.SaveChanges();
        return player.Id;
    }

    private static void SeedUnlinkedSearchablePlayerListing(
        TryOutSpotWebApplicationFactory factory,
        Guid ownerId,
        string title)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        dbContext.PlayerListings.Add(new PlayerListing
        {
            Id = Guid.NewGuid(),
            UserId = ownerId,
            ListingType = TryOutSpotPlayerListingTypes.UsedEquipment,
            Title = title,
            Description = "This listing is not tied to a player profile.",
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

    private static void SeedZipCodeGeographies(
        TryOutSpotWebApplicationFactory factory,
        params ZipCodeGeography[] geographies)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        dbContext.ZipCodeGeographies.AddRange(geographies);
        dbContext.SaveChanges();
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
}

