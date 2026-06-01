using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Listings;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Tests;

public sealed class AdminCenterPageTests
{
    [Fact]
    public async Task BootstrapAdminSeeder_CreatesConfiguredPlatformAdmin()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var email = $"seeded-admin-{Guid.NewGuid():N}@example.com";
        const string password = "SeededAdmin2026!";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BootstrapAdmin:Email"] = email,
                ["BootstrapAdmin:Password"] = password,
                ["BootstrapAdmin:FirstName"] = "Seeded",
                ["BootstrapAdmin:LastName"] = "Admin"
            })
            .Build();
        var logger = factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("BootstrapAdminSeederTests");

        await BootstrapAdminSeeder.SeedAsync(factory.Services, configuration, logger);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var user = await userManager.FindByEmailAsync(email);

        Assert.NotNull(user);
        Assert.Equal("Seeded", user.FirstName);
        Assert.Equal("Admin", user.LastName);
        Assert.True(user.EmailConfirmed);
        Assert.True(user.IsActive);
        Assert.True(await roleManager.RoleExistsAsync(TryOutSpotRoles.PlatformAdmin));
        Assert.True(await userManager.IsInRoleAsync(user, TryOutSpotRoles.PlatformAdmin));
        Assert.True(await userManager.CheckPasswordAsync(user, password));
    }

    [Fact]
    public async Task PlatformAdmin_CanUseAdminCenterPagesAndReviewReport()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var owner = await factory.CreateUserAsync("admin-ui-owner@example.com", [TryOutSpotRoles.Parent]);
        var teamOwner = await factory.CreateUserAsync("admin-ui-team-owner@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var reporter = await factory.CreateUserAsync("admin-ui-reporter@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var admin = await factory.CreateUserAsync("admin-ui-admin@example.com", [TryOutSpotRoles.PlatformAdmin]);
        var sportId = GetActiveSportId(factory);
        var playerId = SeedPlayerProfile(factory, owner.Id, sportId);
        var playerListingId = SeedPlayerListing(factory, owner.Id, sportId, playerId, "Admin UI reported pickup listing");
        var (teamId, opportunityId) = SeedTeamOpportunity(factory, teamOwner.Id, sportId, "Admin UI Aces", "Admin UI reported tryout");
        var reportId = SeedListingReport(factory, reporter.Id, playerListingId: playerListingId);
        SeedListingReport(factory, reporter.Id, opportunityId: opportunityId);
        SeedLaunchPromotionClaim(factory, owner.Id);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        await LoginWebUserAsync(client, admin.Email!);

        await AssertPageContainsAsync(client, "/admin", "Admin center");
        await AssertPageContainsAsync(client, "/admin/promotions", owner.Email!);
        await AssertPageContainsAsync(client, "/admin/promotions", "Claims remaining");
        await AssertPageContainsAsync(client, "/admin/promotions", "Promotion settings");
        await AssertPageContainsAsync(client, "/admin/promotions", "Offer visibility");
        await AssertPageContainsAsync(client, "/admin/promotions", "Reset promo counter");
        await AssertPageContainsAsync(client, "/admin/reports", "Admin UI reported pickup listing");
        await AssertPageContainsAsync(client, $"/admin/reports/{reportId}", "Review action");
        await AssertPageContainsAsync(client, "/admin/flyer-imports", "Flyer imports");
        await AssertPageContainsAsync(client, "/admin/flyer-imports/new", "Add flyer");
        await AssertPageContainsAsync(client, "/admin/users", owner.Email!);
        await AssertPageContainsAsync(client, $"/admin/users/{owner.Id}", "Player profiles");
        await AssertPageContainsAsync(client, $"/admin/users/{owner.Id}", "Complimentary access");
        await AssertPageContainsAsync(client, "/admin/teams?q=Admin%20UI%20Aces", "Admin UI Aces");
        await AssertPageContainsAsync(client, "/admin/player-listings", "Admin UI reported pickup listing");
        await AssertPageContainsAsync(client, "/admin/team-opportunities", "Admin UI reported tryout");

        var promotionToken = await GetAntiForgeryTokenAsync(client, "/admin/promotions");
        var promotionSettingsResponse = await client.PostAsync(
            "/admin/promotions/settings",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", promotionToken),
                new("Name", "Admin UI launch wave"),
                new("IsEnabled", "true"),
                new("MaxRedemptions", "7"),
                new("GrantMonths", "3")
            ]));

        Assert.Equal(HttpStatusCode.Redirect, promotionSettingsResponse.StatusCode);
        Assert.Equal("/admin/promotions", promotionSettingsResponse.Headers.Location?.ToString());

        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, $"/admin/reports/{reportId}");
        var response = await client.PostAsync(
            $"/admin/reports/{reportId}/review",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", antiForgeryToken),
                new("Status", TryOutSpotListingReportStatuses.ActionTaken),
                new("AdminNotes", "Reviewed from the admin center page.")
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/admin/reports/{reportId}", response.Headers.Location?.ToString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var report = await dbContext.ListingReports.SingleAsync(currentReport => currentReport.Id == reportId);
        Assert.Equal(TryOutSpotListingReportStatuses.ActionTaken, report.Status);
        Assert.Equal("Reviewed from the admin center page.", report.AdminNotes);
        Assert.Equal(admin.Id, report.ReviewedByUserId);
        Assert.NotNull(report.ReviewedAt);

        var activePromotion = await dbContext.PromotionCampaigns.SingleAsync(campaign => campaign.IsActive);
        Assert.Equal("Admin UI launch wave", activePromotion.Name);
        Assert.Equal(7, activePromotion.MaxRedemptions);
        Assert.Equal(3, activePromotion.GrantMonths);
        Assert.Equal(admin.Id, activePromotion.UpdatedByUserId);
    }

    [Fact]
    public async Task PlatformAdmin_CanAnalyzeFlyerImageBeforeReviewingImport()
    {
        await using var factory = new TryOutSpotWebApplicationFactory(services =>
        {
            services.RemoveAll<IFlyerAiExtractionService>();
            services.RemoveAll<IPdfStorageService>();
            services.AddScoped<IFlyerAiExtractionService, TestFlyerAiExtractionService>();
            services.AddSingleton<TestFlyerStorageService>();
            services.AddScoped<IPdfStorageService>(serviceProvider =>
                serviceProvider.GetRequiredService<TestFlyerStorageService>());
        });
        var admin = await factory.CreateUserAsync("admin-flyer-ai@example.com", [TryOutSpotRoles.PlatformAdmin]);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        await LoginWebUserAsync(client, admin.Email!);

        var token = await GetAntiForgeryTokenAsync(client, "/admin/flyer-imports/new");
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(token), "__RequestVerificationToken");
        form.Add(new StringContent("facebook"), "Form.SourcePlatform");
        form.Add(new StringContent("https://facebook.test/posts/tryout-flyer"), "Form.SourceUrl");

        var flyerBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10 };
        var flyerContent = new ByteArrayContent(flyerBytes);
        flyerContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(flyerContent, "Form.FlyerFile", "aces-flyer.jpg");

        var response = await client.PostAsync("/admin/flyer-imports", form);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Matches("^/admin/flyer-imports/[0-9a-fA-F-]+$", response.Headers.Location?.ToString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var flyerImport = await dbContext.FlyerImports.SingleAsync();

        Assert.Equal("facebook", flyerImport.SourcePlatform);
        Assert.Equal("Kansas City Aces 14U tryout", flyerImport.Title);
        Assert.Equal("Kansas City Aces", flyerImport.TeamName);
        Assert.Equal("tryout", flyerImport.OpportunityType);
        Assert.Equal("Softball", flyerImport.SportName);
        Assert.Equal("66202", flyerImport.ZipCode);
        Assert.Equal(TryOutSpotFlyerImportStatuses.PendingReview, flyerImport.Status);
        Assert.NotNull(flyerImport.StoredObjectKey);
        Assert.Contains("\"confidenceScore\":0.94", flyerImport.ConfidenceJson);
    }

    [Fact]
    public async Task PlatformAdmin_SeesDuplicateWarningBeforeCreatingFlyerListing()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var admin = await factory.CreateUserAsync("admin-duplicate-flyer@example.com", [TryOutSpotRoles.PlatformAdmin]);
        var sportId = GetActiveSportId(factory);
        var teamName = $"Admin Duplicate Eagles {Guid.NewGuid():N}";
        var title = $"{teamName} 12U tryouts";
        var eventDate = DateTime.UtcNow.Date.AddDays(12).AddHours(18);
        SeedPendingFlyerImport(
            factory,
            admin.Id,
            sportId,
            teamName,
            title,
            eventDate,
            "https://facebook.test/groups/softball/posts/original");
        var duplicateImportId = SeedPendingFlyerImport(
            factory,
            admin.Id,
            sportId,
            teamName,
            title,
            eventDate,
            "https://facebook.test/groups/softball/posts/repost");
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        await LoginWebUserAsync(client, admin.Email!);

        var detailPath = $"/admin/flyer-imports/{duplicateImportId}";
        var detailResponse = await client.GetAsync(detailPath);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var html = await detailResponse.Content.ReadAsStringAsync();
        Assert.Contains("Likely duplicate", html);
        Assert.Contains("100% match", html);
        Assert.Contains("same contact email and phone", html);
        Assert.Contains("Create listing anyway", html);
        Assert.Contains("Skip duplicate", html);

        var token = await GetAntiForgeryTokenAsync(client, detailPath);
        var blockedResponse = await client.PostAsync(
            $"/admin/flyer-imports/{duplicateImportId}/create-listing",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", token),
                new("PublishImmediately", "false")
            ]));

        Assert.Equal(HttpStatusCode.Redirect, blockedResponse.StatusCode);
        Assert.Equal(detailPath, blockedResponse.Headers.Location?.ToString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await dbContext.Opportunities.AnyAsync(opportunity => opportunity.Title == title));
        var duplicateImport = await dbContext.FlyerImports.SingleAsync(currentImport => currentImport.Id == duplicateImportId);
        Assert.Null(duplicateImport.OpportunityId);
    }

    [Fact]
    public async Task PlatformAdmin_CanPublishFlyerCreatedDraftTeamOpportunity()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var teamOwner = await factory.CreateUserAsync("admin-publish-team-owner@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var admin = await factory.CreateUserAsync("admin-publish-admin@example.com", [TryOutSpotRoles.PlatformAdmin]);
        var sportId = GetActiveSportId(factory);
        var (teamId, opportunityId) = SeedTeamOpportunity(
            factory,
            teamOwner.Id,
            sportId,
            "Admin Publish Eagles",
            "Admin Publish Eagles 12U tryout");
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var opportunity = await dbContext.Opportunities.SingleAsync(currentOpportunity => currentOpportunity.Id == opportunityId);
            var team = await dbContext.Teams.SingleAsync(currentTeam => currentTeam.Id == teamId);

            opportunity.IsPublished = false;
            opportunity.PublishedAt = null;
            opportunity.ListingStartDate = null;
            opportunity.EventDate = DateTime.UtcNow.AddDays(14);
            opportunity.ExpiresAt = DateTime.UtcNow.AddDays(14);
            team.IsSearchable = false;
            await dbContext.SaveChangesAsync();
        }
        SeedFlyerImportForOpportunity(
            factory,
            admin.Id,
            teamId,
            opportunityId,
            sportId,
            "Admin Publish Eagles",
            "Admin Publish Eagles 12U tryout");

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, admin.Email!);

        await AssertPageContainsAsync(client, "/admin/team-opportunities?q=Admin%20Publish%20Eagles", "Publish");

        var token = await GetAntiForgeryTokenAsync(client, "/admin/team-opportunities?q=Admin%20Publish%20Eagles");
        var response = await client.PostAsync(
            $"/admin/team-opportunities/{opportunityId}/publish",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", token),
                new("returnUrl", "/admin/team-opportunities?q=Admin%20Publish%20Eagles")
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/team-opportunities?q=Admin%20Publish%20Eagles", response.Headers.Location?.ToString());

        using var verificationScope = factory.Services.CreateScope();
        var verificationDbContext = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var publishedOpportunity = await verificationDbContext.Opportunities.SingleAsync(currentOpportunity => currentOpportunity.Id == opportunityId);
        var publishedTeam = await verificationDbContext.Teams.SingleAsync(currentTeam => currentTeam.Id == teamId);

        Assert.True(publishedOpportunity.IsPublished);
        Assert.True(publishedOpportunity.IsActive);
        Assert.NotNull(publishedOpportunity.PublishedAt);
        Assert.NotNull(publishedOpportunity.ListingStartDate);
        Assert.True(publishedTeam.IsSearchable);
        Assert.True(publishedTeam.IsActive);
    }

    [Fact]
    public async Task PlatformAdmin_CannotPublishTeamCreatedDraftTeamOpportunity()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var teamOwner = await factory.CreateUserAsync("admin-publish-block-team-owner@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var admin = await factory.CreateUserAsync("admin-publish-block-admin@example.com", [TryOutSpotRoles.PlatformAdmin]);
        var sportId = GetActiveSportId(factory);
        var (teamId, opportunityId) = SeedTeamOpportunity(
            factory,
            teamOwner.Id,
            sportId,
            "Admin Publish Block Eagles",
            "Admin Publish Block Eagles 12U tryout");
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var opportunity = await dbContext.Opportunities.SingleAsync(currentOpportunity => currentOpportunity.Id == opportunityId);
            var team = await dbContext.Teams.SingleAsync(currentTeam => currentTeam.Id == teamId);

            opportunity.IsPublished = false;
            opportunity.PublishedAt = null;
            opportunity.ListingStartDate = null;
            opportunity.EventDate = DateTime.UtcNow.AddDays(14);
            opportunity.ExpiresAt = DateTime.UtcNow.AddDays(14);
            team.IsSearchable = false;
            await dbContext.SaveChangesAsync();
        }

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, admin.Email!);

        var token = await GetAntiForgeryTokenAsync(client, "/admin/team-opportunities?q=Admin%20Publish%20Block%20Eagles");
        var response = await client.PostAsync(
            $"/admin/team-opportunities/{opportunityId}/publish",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", token),
                new("returnUrl", "/admin/team-opportunities?q=Admin%20Publish%20Block%20Eagles")
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/team-opportunities?q=Admin%20Publish%20Block%20Eagles", response.Headers.Location?.ToString());

        using var verificationScope = factory.Services.CreateScope();
        var verificationDbContext = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var blockedOpportunity = await verificationDbContext.Opportunities.SingleAsync(currentOpportunity => currentOpportunity.Id == opportunityId);
        var blockedTeam = await verificationDbContext.Teams.SingleAsync(currentTeam => currentTeam.Id == teamId);

        Assert.False(blockedOpportunity.IsPublished);
        Assert.Null(blockedOpportunity.PublishedAt);
        Assert.Null(blockedOpportunity.ListingStartDate);
        Assert.False(blockedTeam.IsSearchable);
    }

    [Fact]
    public async Task PlatformAdmin_CanEditDraftTeamOpportunityLocationAndPublish()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var teamOwner = await factory.CreateUserAsync("admin-edit-team-owner@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var admin = await factory.CreateUserAsync("admin-edit-admin@example.com", [TryOutSpotRoles.PlatformAdmin]);
        var sportId = GetActiveSportId(factory);
        var (teamId, opportunityId) = SeedTeamOpportunity(
            factory,
            teamOwner.Id,
            sportId,
            "Admin Edit Eagles",
            "Admin Edit Eagles 12U tryout");
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var opportunity = await dbContext.Opportunities.SingleAsync(currentOpportunity => currentOpportunity.Id == opportunityId);
            var team = await dbContext.Teams.SingleAsync(currentTeam => currentTeam.Id == teamId);

            opportunity.IsPublished = false;
            opportunity.PublishedAt = null;
            opportunity.EventDate = null;
            opportunity.Location = null;
            opportunity.Address = null;
            opportunity.City = null;
            opportunity.State = null;
            opportunity.ZipCode = null;
            team.IsSearchable = false;
            team.City = null;
            team.State = null;
            team.ZipCode = null;
            await dbContext.SaveChangesAsync();
        }
        SeedFlyerImportForOpportunity(
            factory,
            admin.Id,
            teamId,
            opportunityId,
            sportId,
            "Admin Edit Eagles",
            "Admin Edit Eagles 12U tryout");

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, admin.Email!);

        var editPath = $"/admin/team-opportunities/{opportunityId}/edit?returnUrl=/admin/flyer-imports";
        await AssertPageContainsAsync(client, editPath, "Listing location");

        var token = await GetAntiForgeryTokenAsync(client, editPath);
        var response = await client.PostAsync(
            $"/admin/team-opportunities/{opportunityId}/edit",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", token),
                new("returnUrl", "/admin/flyer-imports"),
                new("Form.TeamName", "Admin Edit Eagles"),
                new("Form.TeamLevel", "12U"),
                new("Form.TeamCity", "Overland Park"),
                new("Form.TeamState", "ks"),
                new("Form.TeamZipCode", "66202"),
                new("Form.Title", "Admin Edit Eagles 12U Softball Tryouts"),
                new("Form.Type", "tryout"),
                new("Form.SportId", sportId.ToString()),
                new("Form.AgeGroup", "12U"),
                new("Form.CompetitionLevel", "A"),
                new("Form.Description", "Edited by the admin listing workflow."),
                new("Form.EventDate", "2030-07-12T18:00:00"),
                new("Form.Location", "Blue Valley Recreation"),
                new("Form.Address", "9701 W 137th St"),
                new("Form.City", "Overland Park"),
                new("Form.State", "ks"),
                new("Form.ZipCode", "66223"),
                new("Form.ContactEmail", "coach@example.com"),
                new("Form.IsActive", "true"),
                new("Form.IsPublished", "true")
            ]));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/admin/flyer-imports", response.Headers.Location?.ToString());

        using var verificationScope = factory.Services.CreateScope();
        var verificationDbContext = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var editedOpportunity = await verificationDbContext.Opportunities
            .Include(currentOpportunity => currentOpportunity.Team)
            .SingleAsync(currentOpportunity => currentOpportunity.Id == opportunityId);

        Assert.True(editedOpportunity.IsPublished);
        Assert.True(editedOpportunity.IsActive);
        Assert.NotNull(editedOpportunity.PublishedAt);
        Assert.Equal("Blue Valley Recreation", editedOpportunity.Location);
        Assert.Equal("Overland Park", editedOpportunity.City);
        Assert.Equal("KS", editedOpportunity.State);
        Assert.Equal("66223", editedOpportunity.ZipCode);
        Assert.True(editedOpportunity.Team.IsSearchable);
        Assert.True(editedOpportunity.Team.IsActive);
        Assert.Equal("Overland Park", editedOpportunity.Team.City);
        Assert.Equal("KS", editedOpportunity.Team.State);
        Assert.Equal("66202", editedOpportunity.Team.ZipCode);
    }

    [Fact]
    public async Task PlatformAdmin_CanViewFlyerFromTeamOpportunityEditPage()
    {
        await using var factory = new TryOutSpotWebApplicationFactory(services =>
        {
            services.RemoveAll<IPdfStorageService>();
            services.AddSingleton<TestFlyerStorageService>();
            services.AddScoped<IPdfStorageService>(serviceProvider =>
                serviceProvider.GetRequiredService<TestFlyerStorageService>());
        });
        var teamOwner = await factory.CreateUserAsync("admin-flyer-edit-team-owner@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var admin = await factory.CreateUserAsync("admin-flyer-edit-admin@example.com", [TryOutSpotRoles.PlatformAdmin]);
        var sportId = GetActiveSportId(factory);
        var (teamId, opportunityId) = SeedTeamOpportunity(
            factory,
            teamOwner.Id,
            sportId,
            "Admin Flyer Eagles",
            "Admin Flyer Eagles 12U tryout");
        var flyerObjectKey = $"opportunities/{opportunityId}/eagles-flyer.jpg";
        var flyerBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x01 };
        using (var scope = factory.Services.CreateScope())
        {
            var storage = scope.ServiceProvider.GetRequiredService<TestFlyerStorageService>();
            await storage.UploadFileAsync(flyerObjectKey, flyerBytes, "image/jpeg", CancellationToken.None);

            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var opportunity = await dbContext.Opportunities.SingleAsync(currentOpportunity => currentOpportunity.Id == opportunityId);
            opportunity.UploadedPdfObjectKey = flyerObjectKey;
            opportunity.UploadedPdfFileName = "eagles-flyer.jpg";
            await dbContext.SaveChangesAsync();
        }

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        await LoginWebUserAsync(client, admin.Email!);

        var editPath = $"/admin/team-opportunities/{opportunityId}/edit";
        var editResponse = await client.GetAsync(editPath);
        Assert.Equal(HttpStatusCode.OK, editResponse.StatusCode);
        var html = await editResponse.Content.ReadAsStringAsync();
        Assert.Contains("Flyer reference", html);
        Assert.Contains($"/admin/team-opportunities/{opportunityId}/flyer", html);
        Assert.Contains("Open full size", html);

        var flyerResponse = await client.GetAsync($"/admin/team-opportunities/{opportunityId}/flyer");
        Assert.Equal(HttpStatusCode.OK, flyerResponse.StatusCode);
        Assert.Equal("image/jpeg", flyerResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal(flyerBytes, await flyerResponse.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task PlatformAdmin_CanSuspendUsersTeamsAndRemoveListings()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var owner = await factory.CreateUserAsync("admin-action-owner@example.com", [TryOutSpotRoles.Parent]);
        var teamOwner = await factory.CreateUserAsync("admin-action-team-owner@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var admin = await factory.CreateUserAsync("admin-action-admin@example.com", [TryOutSpotRoles.PlatformAdmin]);
        var sportId = GetActiveSportId(factory);
        var playerId = SeedPlayerProfile(factory, owner.Id, sportId);
        var playerListingId = SeedPlayerListing(factory, owner.Id, sportId, playerId, "Admin action pickup listing");
        var (teamId, opportunityId) = SeedTeamOpportunity(factory, teamOwner.Id, sportId, "Admin Action Aces", "Admin action tryout");
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        await LoginWebUserAsync(client, admin.Email!);

        var usersToken = await GetAntiForgeryTokenAsync(client, "/admin/users");
        var suspendUserResponse = await client.PostAsync(
            $"/admin/users/{owner.Id}/suspend",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", usersToken),
                new("returnUrl", "/admin/users?q=admin-action-owner")
            ]));
        Assert.Equal(HttpStatusCode.Redirect, suspendUserResponse.StatusCode);
        Assert.Equal("/admin/users?q=admin-action-owner", suspendUserResponse.Headers.Location?.ToString());

        var teamsToken = await GetAntiForgeryTokenAsync(client, "/admin/teams");
        var suspendTeamResponse = await client.PostAsync(
            $"/admin/teams/{teamId}/suspend",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", teamsToken),
                new("returnUrl", "/admin/teams?q=Admin%20Action")
            ]));
        Assert.Equal(HttpStatusCode.Redirect, suspendTeamResponse.StatusCode);
        Assert.Equal("/admin/teams?q=Admin%20Action", suspendTeamResponse.Headers.Location?.ToString());

        var listingsToken = await GetAntiForgeryTokenAsync(client, "/admin/player-listings");
        var deletePlayerListingResponse = await client.PostAsync(
            $"/admin/player-listings/{playerListingId}/delete",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", listingsToken),
                new("returnUrl", "/admin/player-listings?q=Admin%20action")
            ]));
        Assert.Equal(HttpStatusCode.Redirect, deletePlayerListingResponse.StatusCode);
        Assert.Equal("/admin/player-listings?q=Admin%20action", deletePlayerListingResponse.Headers.Location?.ToString());

        var opportunitiesToken = await GetAntiForgeryTokenAsync(client, "/admin/team-opportunities");
        var deactivateOpportunityResponse = await client.PostAsync(
            $"/admin/team-opportunities/{opportunityId}/deactivate",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", opportunitiesToken),
                new("returnUrl", "/admin/team-opportunities?q=Admin%20action")
            ]));
        Assert.Equal(HttpStatusCode.Redirect, deactivateOpportunityResponse.StatusCode);
        Assert.Equal("/admin/team-opportunities?q=Admin%20action", deactivateOpportunityResponse.Headers.Location?.ToString());

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await dbContext.Users.SingleAsync(currentUser => currentUser.Id == owner.Id);
        var team = await dbContext.Teams.SingleAsync(currentTeam => currentTeam.Id == teamId);
        var playerListing = await dbContext.PlayerListings.SingleAsync(listing => listing.Id == playerListingId);
        var opportunity = await dbContext.Opportunities.SingleAsync(currentOpportunity => currentOpportunity.Id == opportunityId);

        Assert.False(user.IsActive);
        Assert.NotNull(user.LockoutEnd);
        Assert.False(team.IsActive);
        Assert.False(playerListing.IsActive);
        Assert.False(playerListing.IsPublished);
        Assert.False(playerListing.IsSearchable);
        Assert.NotNull(playerListing.ExpiresAt);
        Assert.False(opportunity.IsActive);
        Assert.False(opportunity.IsPublished);
    }

    [Fact]
    public async Task PlatformAdmin_CanGrantAndRevokeComplimentaryAccessFromUserProfile()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var user = await factory.CreateUserAsync("admin-comp-grant-user@example.com", [TryOutSpotRoles.Parent]);
        var admin = await factory.CreateUserAsync("admin-comp-grant-admin@example.com", [TryOutSpotRoles.PlatformAdmin]);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        await LoginWebUserAsync(client, admin.Email!);

        var userProfilePath = $"/admin/users/{user.Id}";
        var grantToken = await GetAntiForgeryTokenAsync(client, userProfilePath);
        var grantResponse = await client.PostAsync(
            $"/admin/users/{user.Id}/complimentary-grants",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", grantToken),
                new("ReturnUrl", userProfilePath),
                new("PlanCode", TryOutSpotPlanCodes.PremiumPlayer),
                new("DurationMonths", "2"),
                new("Reason", "Admin UI comp")
            ]));

        Assert.Equal(HttpStatusCode.Redirect, grantResponse.StatusCode);
        Assert.Equal(userProfilePath, grantResponse.Headers.Location?.ToString());

        Guid grantId;
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var grant = await dbContext.ComplimentaryPlanGrants.SingleAsync(currentGrant => currentGrant.UserId == user.Id);
            grantId = grant.Id;

            Assert.Equal(TryOutSpotPlanCodes.PremiumPlayer, grant.PlanType);
            Assert.Equal(TryOutSpotPromotionCodes.AdminComplimentaryGrantSource, grant.Source);
            Assert.Equal(admin.Id, grant.GrantedByUserId);
            Assert.Null(grant.RevokedAt);

            var entitlementService = scope.ServiceProvider.GetRequiredService<IEntitlementService>();
            var entitlements = await entitlementService.GetEntitlementsAsync(user.Id, CancellationToken.None);
            Assert.NotNull(entitlements);
            Assert.Contains(TryOutSpotFeatureCodes.PriorityApplicationReview, entitlements.FeatureCodes);
        }

        var revokeToken = await GetAntiForgeryTokenAsync(client, userProfilePath);
        var revokeResponse = await client.PostAsync(
            $"/admin/users/{user.Id}/complimentary-grants/{grantId}/revoke",
            new FormUrlEncodedContent(
            [
                new("__RequestVerificationToken", revokeToken),
                new("ReturnUrl", userProfilePath),
                new("Reason", "Admin UI revoke")
            ]));

        Assert.Equal(HttpStatusCode.Redirect, revokeResponse.StatusCode);
        Assert.Equal(userProfilePath, revokeResponse.Headers.Location?.ToString());

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var grant = await dbContext.ComplimentaryPlanGrants.SingleAsync(currentGrant => currentGrant.Id == grantId);

            Assert.NotNull(grant.RevokedAt);
            Assert.Equal(admin.Id, grant.RevokedByUserId);
            Assert.Equal("Admin UI revoke", grant.RevokeReason);

            var entitlementService = scope.ServiceProvider.GetRequiredService<IEntitlementService>();
            var entitlements = await entitlementService.GetEntitlementsAsync(user.Id, CancellationToken.None);
            Assert.NotNull(entitlements);
            Assert.DoesNotContain(TryOutSpotFeatureCodes.PriorityApplicationReview, entitlements.FeatureCodes);
        }
    }

    private static async Task AssertPageContainsAsync(HttpClient client, string path, string expectedText)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(expectedText, html);
    }

    private static async Task LoginWebUserAsync(HttpClient client, string email)
    {
        var antiForgeryToken = await GetAntiForgeryTokenAsync(client, "/account/login");
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

    private static async Task<string> GetAntiForgeryTokenAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);

        return match.Success
            ? match.Groups[1].Value
            : throw new InvalidOperationException($"No anti-forgery token was found on {path}.");
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

    private static Guid SeedPlayerProfile(TryOutSpotWebApplicationFactory factory, Guid userId, Guid sportId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var player = new Player
        {
            Id = Guid.NewGuid(),
            FirstName = "Riley",
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

    private sealed class TestFlyerAiExtractionService : IFlyerAiExtractionService
    {
        public Task<FlyerAiExtractionResult> ExtractAsync(
            UploadedFlyerImportFile uploadedFile,
            string? sourceUrl,
            string? externalImageUrl,
            CancellationToken cancellationToken)
        {
            Assert.Equal("image/jpeg", uploadedFile.ContentType);
            var extractedJson = """
                {
                  "title": "Kansas City Aces 14U tryout",
                  "teamName": "Kansas City Aces",
                  "sportName": "Softball",
                  "opportunityType": "tryout",
                  "ageGroup": "14U",
                  "eventDate": "2026-07-12T18:00:00",
                  "city": "Overland Park",
                  "state": "KS",
                  "zipCode": "66202",
                  "confidenceScore": 0.94,
                  "warnings": null
                }
                """;
            var confidenceJson = """
                {"confidenceScore":0.94,"warnings":null}
                """;
            var input = new FlyerImportCreateInput(
                "facebook",
                sourceUrl,
                externalImageUrl,
                SportId: null,
                "Softball",
                "tryout",
                "Kansas City Aces 14U tryout",
                "Kansas City Aces",
                OrganizationName: null,
                "14U",
                CompetitionLevel: null,
                new DateTime(2026, 7, 12, 18, 0, 0),
                EventEndDate: null,
                RegistrationDeadline: null,
                RegistrationFee: null,
                Location: null,
                Address: null,
                "Overland Park",
                "KS",
                "66202",
                ContactEmail: null,
                ContactPhone: null,
                WebsiteUrl: null,
                Description: null,
                RequiredEquipment: null,
                WhatToBring: null,
                SpecialInstructions: null,
                extractedJson,
                confidenceJson,
                AdminNotes: null);

            return Task.FromResult(FlyerAiExtractionResult.Success(input, extractedJson, confidenceJson));
        }
    }

    private sealed class TestFlyerStorageService : IPdfStorageService
    {
        private readonly Dictionary<string, StoredObjectPayload> storedObjects = [];

        public Task UploadPdfAsync(string objectKey, byte[] content, CancellationToken cancellationToken)
        {
            return UploadFileAsync(objectKey, content, "application/pdf", cancellationToken);
        }

        public Task UploadFileAsync(
            string objectKey,
            byte[] content,
            string contentType,
            CancellationToken cancellationToken)
        {
            storedObjects[objectKey] = new StoredObjectPayload(content, contentType);
            return Task.CompletedTask;
        }

        public Task<byte[]?> DownloadPdfAsync(string objectKey, CancellationToken cancellationToken)
        {
            return Task.FromResult(storedObjects.TryGetValue(objectKey, out var payload)
                ? payload.Content
                : null);
        }

        public Task<StoredObjectPayload?> DownloadFileAsync(string objectKey, CancellationToken cancellationToken)
        {
            return Task.FromResult(storedObjects.GetValueOrDefault(objectKey));
        }

        public Task DeletePdfAsync(string objectKey, CancellationToken cancellationToken)
        {
            storedObjects.Remove(objectKey);
            return Task.CompletedTask;
        }
    }

    private static Guid SeedPlayerListing(
        TryOutSpotWebApplicationFactory factory,
        Guid userId,
        Guid sportId,
        Guid playerId,
        string title)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var listing = new PlayerListing
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PlayerId = playerId,
            SportId = sportId,
            ListingType = TryOutSpotPlayerListingTypes.PickupPlayer,
            Title = title,
            Description = "Seeded listing for admin center page tests.",
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
            GeographicScope = "Regional",
            Description = "Seeded team for admin center page tests.",
            City = "Wichita",
            State = "KS",
            ZipCode = "67202",
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
            Description = "Seeded opportunity for admin center page tests.",
            City = "Wichita",
            State = "KS",
            ZipCode = "67202",
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
            CompetitionLevel = "A",
            AgeGroup = "14U",
            TravelLevel = "Regional",
            IsActive = true,
            CreatedAt = now
        });
        dbContext.UserTeamRoles.Add(new UserTeamRole
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TeamId = team.Id,
            Role = "Owner",
            StartDate = now,
            IsActive = true,
            CreatedAt = now
        });
        dbContext.Opportunities.Add(opportunity);
        dbContext.SaveChanges();
        return (team.Id, opportunity.Id);
    }

    private static void SeedFlyerImportForOpportunity(
        TryOutSpotWebApplicationFactory factory,
        Guid adminUserId,
        Guid teamId,
        Guid opportunityId,
        Guid sportId,
        string teamName,
        string title)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        dbContext.FlyerImports.Add(new FlyerImport
        {
            Id = Guid.NewGuid(),
            SourcePlatform = "facebook",
            SourceUrl = "https://facebook.test/posts/admin-flyer",
            Status = TryOutSpotFlyerImportStatuses.DraftCreated,
            SportId = sportId,
            SportName = "Softball",
            OpportunityType = "tryout",
            Title = title,
            TeamName = teamName,
            AgeGroup = "12U",
            EventDate = now.AddDays(14),
            City = "Wichita",
            State = "KS",
            ZipCode = "67202",
            ContactEmail = "coach@example.com",
            Description = "Seeded flyer import for admin publishing tests.",
            CreatedByUserId = adminUserId,
            ReviewedByUserId = adminUserId,
            ReviewedAt = now,
            TeamId = teamId,
            OpportunityId = opportunityId,
            CreatedAt = now,
            UpdatedAt = now
        });
        dbContext.SaveChanges();
    }

    private static Guid SeedPendingFlyerImport(
        TryOutSpotWebApplicationFactory factory,
        Guid adminUserId,
        Guid sportId,
        string teamName,
        string title,
        DateTime eventDate,
        string sourceUrl)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var flyerImportId = Guid.NewGuid();

        dbContext.FlyerImports.Add(new FlyerImport
        {
            Id = flyerImportId,
            SourcePlatform = "facebook",
            SourceUrl = sourceUrl,
            OriginalExternalImageUrl = sourceUrl.Replace("posts", "images", StringComparison.Ordinal),
            Status = TryOutSpotFlyerImportStatuses.PendingReview,
            SportId = sportId,
            SportName = "Softball",
            OpportunityType = "tryout",
            Title = title,
            TeamName = teamName,
            AgeGroup = "12U",
            EventDate = eventDate,
            City = "Kansas City",
            State = "MO",
            ZipCode = "64153",
            ContactEmail = "duplicate-coach@example.test",
            ContactPhone = "816-555-1212",
            Description = "Seeded pending flyer import for duplicate review tests.",
            CreatedByUserId = adminUserId,
            CreatedAt = now,
            UpdatedAt = now
        });
        dbContext.SaveChanges();
        return flyerImportId;
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
            Details = "Seeded report for the admin center page tests.",
            Status = TryOutSpotListingReportStatuses.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.ListingReports.Add(report);
        dbContext.SaveChanges();
        return report.Id;
    }

    private static void SeedLaunchPromotionClaim(TryOutSpotWebApplicationFactory factory, Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var endsAt = now.AddMonths(TryOutSpotPromotionCodes.LaunchFirst1000GrantMonths);

        dbContext.ComplimentaryPlanGrants.Add(new ComplimentaryPlanGrant
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PlanType = TryOutSpotPlanCodes.PremiumPlayer,
            ScopeType = TryOutSpotSubscriptionScopeTypes.Account,
            StartsAt = now,
            EndsAt = endsAt,
            Source = TryOutSpotPromotionCodes.LaunchPromotionGrantSource,
            PromotionCode = TryOutSpotPromotionCodes.LaunchFirst1000TwoMonths,
            Reason = "Launch promotion: first 1000 users receive two free months.",
            GrantedByUserId = userId,
            CreatedAt = now,
            UpdatedAt = now
        });
        dbContext.PromotionRedemptions.Add(new PromotionRedemption
        {
            Id = Guid.NewGuid(),
            PromotionCode = TryOutSpotPromotionCodes.LaunchFirst1000TwoMonths,
            UserId = userId,
            GrantedPlanCodes = TryOutSpotPlanCodes.PremiumPlayer,
            RedeemedAt = now
        });
        dbContext.SaveChanges();
    }
}
