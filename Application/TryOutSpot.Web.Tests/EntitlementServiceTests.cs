using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Security;
using TryOutSpot.Web.Models.Billing;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Tests;

public sealed class EntitlementServiceTests
{
    [Fact]
    public async Task BillingCatalogEndpoints_ReturnPlansAndFeatures()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var client = factory.CreateClient();

        var plansResponse = await client.GetAsync("/api/billing/plans");
        var featuresResponse = await client.GetAsync("/api/billing/features");

        Assert.Equal(HttpStatusCode.OK, plansResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, featuresResponse.StatusCode);

        var plans = await plansResponse.Content.ReadFromJsonAsync<BillingPlanResponse[]>();
        var features = await featuresResponse.Content.ReadFromJsonAsync<BillingFeatureResponse[]>();

        Assert.NotNull(plans);
        Assert.NotNull(features);
        Assert.Contains(plans, plan => plan.Code == TryOutSpotPlanCodes.PremiumPlayer);
        Assert.Contains(features, feature => feature.Code == TryOutSpotFeatureCodes.BrowseOpportunities);
    }

    [Fact]
    public async Task ParentWithoutSubscription_GetsOnlyFreePlayerParentFeatures()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var userId = await factory.RegisterUserAsync("free-parent@example.com", ["Parent"]);

        using var scope = factory.Services.CreateScope();
        var entitlementService = scope.ServiceProvider.GetRequiredService<IEntitlementService>();

        var entitlements = await entitlementService.GetEntitlementsAsync(userId, CancellationToken.None);

        Assert.NotNull(entitlements);
        Assert.Contains(TryOutSpotFeatureCodes.BrowseOpportunities, entitlements.FeatureCodes);
        Assert.Contains(TryOutSpotFeatureCodes.ApplyToOpportunities, entitlements.FeatureCodes);
        Assert.Contains(TryOutSpotFeatureCodes.CreatePlayerListings, entitlements.FeatureCodes);
        Assert.DoesNotContain(TryOutSpotFeatureCodes.PriorityApplicationReview, entitlements.FeatureCodes);
        Assert.DoesNotContain(TryOutSpotFeatureCodes.AdvancedPlayerSearch, entitlements.FeatureCodes);
    }

    [Fact]
    public async Task ActivePremiumSubscription_AddsPaidPlayerFeaturesToRoleFeatures()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var userId = await factory.RegisterUserAsync("premium-parent-coach@example.com", ["Parent", "TeamRepresentative"]);
        await AddSubscriptionAsync(factory, userId, TryOutSpotPlanCodes.PremiumPlayer, "active");

        using var scope = factory.Services.CreateScope();
        var entitlementService = scope.ServiceProvider.GetRequiredService<IEntitlementService>();

        var entitlements = await entitlementService.GetEntitlementsAsync(userId, CancellationToken.None);

        Assert.NotNull(entitlements);
        Assert.Contains("Parent", entitlements.AccountTypes);
        Assert.Contains("TeamRepresentative", entitlements.AccountTypes);
        Assert.Contains(TryOutSpotFeatureCodes.BrowseOpportunities, entitlements.FeatureCodes);
        Assert.Contains(TryOutSpotFeatureCodes.PriorityApplicationReview, entitlements.FeatureCodes);
        Assert.Contains(TryOutSpotFeatureCodes.AdvancedOpportunitySearch, entitlements.FeatureCodes);
    }

    [Fact]
    public async Task PastDueSubscription_DoesNotGrantPaidFeatures()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var userId = await factory.RegisterUserAsync("past-due@example.com", ["Parent"]);
        await AddSubscriptionAsync(factory, userId, TryOutSpotPlanCodes.PremiumPlayer, "past_due");

        using var scope = factory.Services.CreateScope();
        var entitlementService = scope.ServiceProvider.GetRequiredService<IEntitlementService>();

        var entitlements = await entitlementService.GetEntitlementsAsync(userId, CancellationToken.None);

        Assert.NotNull(entitlements);
        Assert.Contains(TryOutSpotFeatureCodes.BrowseOpportunities, entitlements.FeatureCodes);
        Assert.DoesNotContain(TryOutSpotFeatureCodes.PriorityApplicationReview, entitlements.FeatureCodes);
    }

    [Fact]
    public async Task ActiveTeamProfessionalSubscription_GrantsTeamFeatures()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var userId = await factory.RegisterUserAsync("team-pro@example.com", ["TeamRepresentative"]);
        await AddSubscriptionAsync(factory, userId, TryOutSpotPlanCodes.TeamProfessional, "active");

        using var scope = factory.Services.CreateScope();
        var entitlementService = scope.ServiceProvider.GetRequiredService<IEntitlementService>();

        var entitlements = await entitlementService.GetEntitlementsAsync(userId, CancellationToken.None);

        Assert.NotNull(entitlements);
        Assert.Contains(TryOutSpotFeatureCodes.UnlimitedOpportunityPostings, entitlements.FeatureCodes);
        Assert.Contains(TryOutSpotFeatureCodes.AdvancedPlayerSearch, entitlements.FeatureCodes);
        Assert.DoesNotContain(TryOutSpotFeatureCodes.BrowseOpportunities, entitlements.FeatureCodes);
    }

    [Fact]
    public async Task ActiveMultipleSubscriptions_CombinePlayerAndTeamFeatures()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var userId = await factory.RegisterUserAsync("multi-sub-parent-coach@example.com", ["Parent", "TeamRepresentative"]);
        await AddSubscriptionAsync(
            factory,
            userId,
            TryOutSpotPlanCodes.PremiumPlayer,
            "active",
            TryOutSpotSubscriptionScopeTypes.Player,
            Guid.NewGuid());
        await AddSubscriptionAsync(
            factory,
            userId,
            TryOutSpotPlanCodes.TeamProfessional,
            "active",
            TryOutSpotSubscriptionScopeTypes.Team,
            Guid.NewGuid());

        using var scope = factory.Services.CreateScope();
        var entitlementService = scope.ServiceProvider.GetRequiredService<IEntitlementService>();

        var entitlements = await entitlementService.GetEntitlementsAsync(userId, CancellationToken.None);

        Assert.NotNull(entitlements);
        Assert.Contains(TryOutSpotPlanCodes.PremiumPlayer, entitlements.ActivePlanCodes);
        Assert.Contains(TryOutSpotPlanCodes.TeamProfessional, entitlements.ActivePlanCodes);
        Assert.Contains(TryOutSpotFeatureCodes.BrowseOpportunities, entitlements.FeatureCodes);
        Assert.Contains(TryOutSpotFeatureCodes.PriorityApplicationReview, entitlements.FeatureCodes);
        Assert.Contains(TryOutSpotFeatureCodes.UnlimitedOpportunityPostings, entitlements.FeatureCodes);
        Assert.Contains(TryOutSpotFeatureCodes.AdvancedPlayerSearch, entitlements.FeatureCodes);
    }

    [Fact]
    public async Task FeatureAuthorizationPolicy_UsesCurrentEntitlements()
    {
        await using var factory = new TryOutSpotWebApplicationFactory();
        var userId = await factory.RegisterUserAsync("feature-policy@example.com", ["Parent"]);

        using var scope = factory.Services.CreateScope();
        var authorizationService = scope.ServiceProvider.GetRequiredService<IAuthorizationService>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())],
            "Test"));

        var freeFeatureResult = await authorizationService.AuthorizeAsync(
            principal,
            null,
            TryOutSpotAuthorizationPolicies.Feature(TryOutSpotFeatureCodes.BrowseOpportunities));
        var paidFeatureBeforeSubscription = await authorizationService.AuthorizeAsync(
            principal,
            null,
            TryOutSpotAuthorizationPolicies.Feature(TryOutSpotFeatureCodes.PriorityApplicationReview));

        Assert.True(freeFeatureResult.Succeeded);
        Assert.False(paidFeatureBeforeSubscription.Succeeded);

        await AddSubscriptionAsync(factory, userId, TryOutSpotPlanCodes.PremiumPlayer, "active");

        var paidFeatureAfterSubscription = await authorizationService.AuthorizeAsync(
            principal,
            null,
            TryOutSpotAuthorizationPolicies.Feature(TryOutSpotFeatureCodes.PriorityApplicationReview));

        Assert.True(paidFeatureAfterSubscription.Succeeded);
    }

    private static async Task AddSubscriptionAsync(
        TryOutSpotWebApplicationFactory factory,
        Guid userId,
        string planType,
        string status,
        string scopeType = TryOutSpotSubscriptionScopeTypes.Account,
        Guid? scopeId = null)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;

        dbContext.Subscriptions.Add(new Subscription
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            PlanType = planType,
            Status = status,
            ScopeType = scopeType,
            ScopeId = scopeId,
            StripeCustomerId = $"cus_{Guid.NewGuid():N}",
            StripeSubscriptionId = $"sub_{Guid.NewGuid():N}",
            CurrentPeriodStart = now,
            CurrentPeriodEnd = now.AddMonths(1),
            Amount = 9.99m,
            Currency = "USD",
            BillingInterval = "month",
            CreatedAt = now,
            UpdatedAt = now
        });

        await dbContext.SaveChangesAsync();
        dbContext.ChangeTracker.Clear();

        Assert.True(await dbContext.Subscriptions.AnyAsync(subscription => subscription.UserId == userId));
    }
}

