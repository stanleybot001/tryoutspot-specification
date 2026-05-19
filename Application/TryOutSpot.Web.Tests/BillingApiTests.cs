using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Stripe;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Identity;
using TryOutSpot.Web.Models.Account;
using TryOutSpot.Web.Models.Billing;
using TryOutSpot.Web.Services;
using AppSubscription = TryOutSpot.Web.Data.Entities.Subscription;

namespace TryOutSpot.Web.Tests;

public sealed class BillingApiTests
{
    [Fact]
    public async Task BillingCatalogEndpoints_ReturnConfiguredPriceOptions()
    {
        await using var factory = CreateFactoryWithStripe();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/billing/plans");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var plans = await response.Content.ReadFromJsonAsync<BillingPlanResponse[]>();
        Assert.NotNull(plans);

        var premiumPlan = Assert.Single(plans, plan => plan.Code == TryOutSpotPlanCodes.PremiumPlayer);
        Assert.Contains(premiumPlan.Prices, price =>
            price.BillingInterval == BillingIntervalCodes.Month
            && price.Amount == 9.99m
            && price.IsCheckoutConfigured);
        Assert.Contains(premiumPlan.Prices, price =>
            price.BillingInterval == BillingIntervalCodes.Year
            && price.Amount == 99m
            && price.IsCheckoutConfigured);
    }

    [Fact]
    public async Task CreateCheckoutSession_RecordsPendingSubscriptionWithoutGrantingPaidAccess()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync("checkout-parent@example.com", [TryOutSpotRoles.Parent]);
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        var response = await client.PostAsJsonAsync(
            "/api/billing/checkout-session",
            new CreateCheckoutSessionRequest(TryOutSpotPlanCodes.PremiumPlayer, BillingIntervalCodes.Month));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var checkout = await response.Content.ReadFromJsonAsync<CheckoutSessionResponse>();
        Assert.NotNull(checkout);
        Assert.Equal("cs_test_checkout", checkout.SessionId);
        Assert.Equal("https://checkout.stripe.test/session", checkout.Url);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscription = await dbContext.Subscriptions.SingleAsync(current => current.UserId == user.Id);

        Assert.Equal("checkout_started", subscription.Status);
        Assert.Equal(TryOutSpotPlanCodes.PremiumPlayer, subscription.PlanType);
        Assert.Equal(TryOutSpotSubscriptionScopeTypes.Account, subscription.ScopeType);
        Assert.Null(subscription.ScopeId);
        Assert.Equal("cus_test_checkout", subscription.StripeCustomerId);
        Assert.Equal("price_premium_month", subscription.StripePriceId);
        Assert.Equal(9.99m, subscription.Amount);

        var entitlementService = scope.ServiceProvider.GetRequiredService<IEntitlementService>();
        var entitlements = await entitlementService.GetEntitlementsAsync(user.Id, CancellationToken.None);

        Assert.NotNull(entitlements);
        Assert.DoesNotContain(TryOutSpotFeatureCodes.PriorityApplicationReview, entitlements.FeatureCodes);
    }

    [Fact]
    public async Task AdminComplimentaryGrant_GrantsAccessWithoutCreatingStripeSubscription()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync("admin-grant-parent@example.com", [TryOutSpotRoles.Parent]);
        var adminClient = await CreateAdminClientAsync(factory);

        var grantResponse = await adminClient.PostAsJsonAsync(
            "/api/admin/billing/grants",
            new CreateComplimentaryPlanGrantRequest(
                user.Id,
                TryOutSpotPlanCodes.PremiumPlayer,
                DurationMonths: 3,
                Reason: "Founder comp"));

        Assert.Equal(HttpStatusCode.Created, grantResponse.StatusCode);
        var grant = await grantResponse.Content.ReadFromJsonAsync<ComplimentaryPlanGrantResponse>();

        Assert.NotNull(grant);
        Assert.Equal(user.Id, grant.UserId);
        Assert.Equal(TryOutSpotPlanCodes.PremiumPlayer, grant.PlanCode);
        Assert.True(grant.HasActiveEntitlement);
        Assert.Equal("complimentary", grant.Status);

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await dbContext.Subscriptions.AnyAsync(subscription => subscription.UserId == user.Id));
        }

        var userClient = await CreateAuthorizedClientAsync(factory, user.Email!);
        var billingResponse = await userClient.GetAsync("/api/billing/me");

        Assert.Equal(HttpStatusCode.OK, billingResponse.StatusCode);
        var billing = await billingResponse.Content.ReadFromJsonAsync<CurrentBillingResponse>();

        Assert.NotNull(billing);
        Assert.Equal(TryOutSpotPlanCodes.PremiumPlayer, billing.PlanCode);
        Assert.Equal("complimentary", billing.Status);
        Assert.True(billing.HasActiveEntitlement);
        Assert.Single(billing.ComplimentaryGrants);
    }

    [Fact]
    public async Task RevokeComplimentaryGrant_RemovesPaidEntitlement()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync("revoke-grant-parent@example.com", [TryOutSpotRoles.Parent]);
        var adminClient = await CreateAdminClientAsync(factory);
        var grantResponse = await adminClient.PostAsJsonAsync(
            "/api/admin/billing/grants",
            new CreateComplimentaryPlanGrantRequest(
                user.Id,
                TryOutSpotPlanCodes.PremiumPlayer,
                DurationMonths: 3,
                Reason: "Temporary comp"));
        grantResponse.EnsureSuccessStatusCode();
        var grant = await grantResponse.Content.ReadFromJsonAsync<ComplimentaryPlanGrantResponse>();
        Assert.NotNull(grant);

        var revokeResponse = await adminClient.PostAsJsonAsync(
            $"/api/admin/billing/grants/{grant.Id}/revoke",
            new RevokeComplimentaryPlanGrantRequest("No longer eligible"));

        Assert.Equal(HttpStatusCode.OK, revokeResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var entitlementService = scope.ServiceProvider.GetRequiredService<IEntitlementService>();
        var entitlements = await entitlementService.GetEntitlementsAsync(user.Id, CancellationToken.None);

        Assert.NotNull(entitlements);
        Assert.DoesNotContain(TryOutSpotFeatureCodes.PriorityApplicationReview, entitlements.FeatureCodes);
    }

    [Fact]
    public async Task LaunchFounderOffer_ClaimsTwoMonthComplimentaryGrant()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync(
            "launch-claim-parent-team@example.com",
            [TryOutSpotRoles.Parent, TryOutSpotRoles.TeamRepresentative]);
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        var response = await client.PostAsync("/api/billing/promotions/launch-founder-offer/claim", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var claim = await response.Content.ReadFromJsonAsync<PromotionClaimResponse>();

        Assert.NotNull(claim);
        Assert.True(claim.Claimed);
        Assert.Equal(TryOutSpotPromotionCodes.LaunchFirst1000TwoMonths, claim.PromotionCode);
        Assert.Equal(2, claim.Grants.Count);
        Assert.Contains(claim.Grants, grant => grant.PlanCode == TryOutSpotPlanCodes.PremiumPlayer);
        Assert.Contains(claim.Grants, grant => grant.PlanCode == TryOutSpotPlanCodes.TeamBasic);
        Assert.All(claim.Grants, grant => Assert.True(grant.HasActiveEntitlement));

        using var scope = factory.Services.CreateScope();
        var entitlementService = scope.ServiceProvider.GetRequiredService<IEntitlementService>();
        var entitlements = await entitlementService.GetEntitlementsAsync(user.Id, CancellationToken.None);

        Assert.NotNull(entitlements);
        Assert.Contains(TryOutSpotFeatureCodes.PriorityApplicationReview, entitlements.FeatureCodes);
        Assert.Contains(TryOutSpotFeatureCodes.AdvancedPlayerSearch, entitlements.FeatureCodes);
    }

    [Fact]
    public async Task AdminLaunchFounderOfferStatus_ReturnsClaimCountsAndRecentClaims()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync(
            "launch-status-parent-team@example.com",
            [TryOutSpotRoles.Parent, TryOutSpotRoles.TeamRepresentative]);
        var userClient = await CreateAuthorizedClientAsync(factory, user.Email!);
        var claimResponse = await userClient.PostAsync("/api/billing/promotions/launch-founder-offer/claim", null);
        claimResponse.EnsureSuccessStatusCode();

        var adminClient = await CreateAdminClientAsync(factory);
        var response = await adminClient.GetAsync("/api/admin/billing/promotions/launch-founder-offer/status?limit=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var status = await response.Content.ReadFromJsonAsync<LaunchPromotionStatusResponse>();

        Assert.NotNull(status);
        Assert.Equal(TryOutSpotPromotionCodes.LaunchFirst1000TwoMonths, status.PromotionCode);
        Assert.Equal(TryOutSpotPromotionCodes.LaunchFounderOfferName, status.PromotionName);
        Assert.Equal(1, status.ClaimedCount);
        Assert.Equal(TryOutSpotPromotionCodes.LaunchFirst1000MaxRedemptions - 1, status.RemainingCount);
        Assert.Equal(TryOutSpotPromotionCodes.LaunchFirst1000MaxRedemptions, status.MaxClaims);
        Assert.Equal(TryOutSpotPromotionCodes.LaunchFirst1000GrantMonths, status.GrantMonths);
        Assert.Equal(2, status.ActiveGrantCount);
        Assert.Single(status.RecentClaims);

        var claim = status.RecentClaims.Single();
        Assert.Equal(user.Id, claim.UserId);
        Assert.Equal(user.Email, claim.UserEmail);
        Assert.Equal(2, claim.ActiveGrantCount);
        Assert.Contains(TryOutSpotPlanCodes.PremiumPlayer, claim.GrantedPlanCodes);
        Assert.Contains(TryOutSpotPlanCodes.TeamBasic, claim.GrantedPlanCodes);
        Assert.NotNull(claim.LatestGrantEndsAt);
    }

    [Fact]
    public async Task AdminLaunchFounderOfferSettings_ControlsFutureClaimLimitAndDuration()
    {
        await using var factory = CreateFactoryWithStripe();
        var adminClient = await CreateAdminClientAsync(factory);

        var settingsResponse = await adminClient.PostAsJsonAsync(
            "/api/admin/billing/promotions/launch-founder-offer/settings",
            new LaunchPromotionSettingsRequest("Spring beta offer", 1, 4));

        Assert.Equal(HttpStatusCode.OK, settingsResponse.StatusCode);
        var settings = await settingsResponse.Content.ReadFromJsonAsync<LaunchPromotionStatusResponse>();
        Assert.NotNull(settings);
        Assert.Equal("Spring beta offer", settings.PromotionName);
        Assert.Equal(1, settings.MaxClaims);
        Assert.Equal(4, settings.GrantMonths);

        var firstUser = await factory.CreateUserAsync("launch-settings-first@example.com", [TryOutSpotRoles.Parent]);
        var firstClient = await CreateAuthorizedClientAsync(factory, firstUser.Email!);
        var firstClaimResponse = await firstClient.PostAsync("/api/billing/promotions/launch-founder-offer/claim", null);

        Assert.Equal(HttpStatusCode.OK, firstClaimResponse.StatusCode);
        var firstClaim = await firstClaimResponse.Content.ReadFromJsonAsync<PromotionClaimResponse>();
        Assert.NotNull(firstClaim);
        Assert.True(firstClaim.Claimed);
        Assert.NotNull(firstClaim.EndsAt);
        Assert.InRange(
            firstClaim.EndsAt.Value,
            DateTime.UtcNow.AddMonths(4).AddMinutes(-5),
            DateTime.UtcNow.AddMonths(4).AddMinutes(5));

        var secondUser = await factory.CreateUserAsync("launch-settings-second@example.com", [TryOutSpotRoles.Parent]);
        var secondClient = await CreateAuthorizedClientAsync(factory, secondUser.Email!);
        var secondClaimResponse = await secondClient.PostAsync("/api/billing/promotions/launch-founder-offer/claim", null);

        Assert.Equal(HttpStatusCode.Conflict, secondClaimResponse.StatusCode);
    }

    [Fact]
    public async Task AdminLaunchFounderOfferReset_StartsFreshCampaignCounter()
    {
        await using var factory = CreateFactoryWithStripe();
        var firstUser = await factory.CreateUserAsync("launch-reset-first@example.com", [TryOutSpotRoles.Parent]);
        var firstClient = await CreateAuthorizedClientAsync(factory, firstUser.Email!);
        var firstClaimResponse = await firstClient.PostAsync("/api/billing/promotions/launch-founder-offer/claim", null);
        firstClaimResponse.EnsureSuccessStatusCode();
        var firstClaim = await firstClaimResponse.Content.ReadFromJsonAsync<PromotionClaimResponse>();
        Assert.NotNull(firstClaim);

        var adminClient = await CreateAdminClientAsync(factory);
        var resetResponse = await adminClient.PostAsJsonAsync(
            "/api/admin/billing/promotions/launch-founder-offer/reset",
            new LaunchPromotionSettingsRequest("Second launch wave", 5, 1));

        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);
        var resetStatus = await resetResponse.Content.ReadFromJsonAsync<LaunchPromotionStatusResponse>();

        Assert.NotNull(resetStatus);
        Assert.NotEqual(firstClaim.PromotionCode, resetStatus.PromotionCode);
        Assert.Equal("Second launch wave", resetStatus.PromotionName);
        Assert.Equal(0, resetStatus.ClaimedCount);
        Assert.Equal(5, resetStatus.RemainingCount);
        Assert.Equal(5, resetStatus.MaxClaims);
        Assert.Equal(1, resetStatus.GrantMonths);
        Assert.Empty(resetStatus.RecentClaims);

        var secondUser = await factory.CreateUserAsync("launch-reset-second@example.com", [TryOutSpotRoles.Parent]);
        var secondClient = await CreateAuthorizedClientAsync(factory, secondUser.Email!);
        var secondClaimResponse = await secondClient.PostAsync("/api/billing/promotions/launch-founder-offer/claim", null);
        var secondClaim = await secondClaimResponse.Content.ReadFromJsonAsync<PromotionClaimResponse>();

        Assert.Equal(HttpStatusCode.OK, secondClaimResponse.StatusCode);
        Assert.NotNull(secondClaim);
        Assert.Equal(resetStatus.PromotionCode, secondClaim.PromotionCode);
    }

    [Fact]
    public async Task LaunchFounderOffer_SecondClaimReturnsExistingPromotionGrants()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync("launch-claim-once-parent@example.com", [TryOutSpotRoles.Parent]);
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        var firstResponse = await client.PostAsync("/api/billing/promotions/launch-founder-offer/claim", null);
        var secondResponse = await client.PostAsync("/api/billing/promotions/launch-founder-offer/claim", null);

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var secondClaim = await secondResponse.Content.ReadFromJsonAsync<PromotionClaimResponse>();
        Assert.NotNull(secondClaim);
        Assert.False(secondClaim.Claimed);
        Assert.Single(secondClaim.Grants);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await dbContext.PromotionRedemptions.CountAsync(redemption => redemption.UserId == user.Id));
        Assert.Equal(1, await dbContext.ComplimentaryPlanGrants.CountAsync(grant => grant.UserId == user.Id));
    }

    [Theory]
    [InlineData(TryOutSpotRoles.Parent, TryOutSpotPlanCodes.TeamBasic)]
    [InlineData(TryOutSpotRoles.TeamRepresentative, TryOutSpotPlanCodes.PremiumPlayer)]
    public async Task CreateCheckoutSession_WithPlanOutsideAccountType_ReturnsBadRequest(
        string accountType,
        string planCode)
    {
        await using var factory = CreateFactoryWithStripe();
        var normalizedRole = accountType.ToLowerInvariant();
        var normalizedPlan = planCode.Replace("_", "-", StringComparison.Ordinal);
        var user = await factory.CreateUserAsync(
            $"checkout-mismatch-{normalizedRole}-{normalizedPlan}@example.com",
            [accountType]);
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        var response = await client.PostAsJsonAsync(
            "/api/billing/checkout-session",
            new CreateCheckoutSessionRequest(planCode, BillingIntervalCodes.Month));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(TryOutSpotRoles.Parent)]
    [InlineData(TryOutSpotRoles.Player)]
    public async Task CreateCheckoutSession_WithPlayerOrParentAndCoach_AllowsPlayerAndTeamPlans(string playerSideRole)
    {
        await using var factory = CreateFactoryWithStripe();
        var normalizedRole = playerSideRole.ToLowerInvariant();
        var user = await factory.CreateUserAsync(
            $"checkout-mixed-{normalizedRole}-coach@example.com",
            [playerSideRole, TryOutSpotRoles.TeamRepresentative]);
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        var premiumResponse = await client.PostAsJsonAsync(
            "/api/billing/checkout-session",
            new CreateCheckoutSessionRequest(TryOutSpotPlanCodes.PremiumPlayer, BillingIntervalCodes.Month));

        Assert.Equal(HttpStatusCode.OK, premiumResponse.StatusCode);

        var teamResponse = await client.PostAsJsonAsync(
            "/api/billing/checkout-session",
            new CreateCheckoutSessionRequest(TryOutSpotPlanCodes.TeamProfessional, BillingIntervalCodes.Year));

        Assert.Equal(HttpStatusCode.OK, teamResponse.StatusCode);
    }

    [Fact]
    public async Task CreateCheckoutSession_WithDifferentPlayerScopes_AllowsMultiplePremiumSubscriptions()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync("checkout-multiple-player-scopes@example.com", [TryOutSpotRoles.Parent]);
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);
        var firstPlayerId = Guid.NewGuid();
        var secondPlayerId = Guid.NewGuid();

        var firstResponse = await client.PostAsJsonAsync(
            "/api/billing/checkout-session",
            new CreateCheckoutSessionRequest(
                TryOutSpotPlanCodes.PremiumPlayer,
                BillingIntervalCodes.Month,
                TryOutSpotSubscriptionScopeTypes.Player,
                firstPlayerId));

        var secondResponse = await client.PostAsJsonAsync(
            "/api/billing/checkout-session",
            new CreateCheckoutSessionRequest(
                TryOutSpotPlanCodes.PremiumPlayer,
                BillingIntervalCodes.Month,
                TryOutSpotSubscriptionScopeTypes.Player,
                secondPlayerId));

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var subscriptions = await dbContext.Subscriptions
            .Where(subscription => subscription.UserId == user.Id)
            .OrderBy(subscription => subscription.ScopeId)
            .ToArrayAsync();

        Assert.Equal(2, subscriptions.Length);
        Assert.All(subscriptions, subscription => Assert.Equal(TryOutSpotPlanCodes.PremiumPlayer, subscription.PlanType));
        Assert.Contains(subscriptions, subscription => subscription.ScopeId == firstPlayerId);
        Assert.Contains(subscriptions, subscription => subscription.ScopeId == secondPlayerId);
        Assert.All(subscriptions, subscription => Assert.Equal(TryOutSpotSubscriptionScopeTypes.Player, subscription.ScopeType));
    }

    [Fact]
    public async Task CreateCheckoutSession_WithActiveSameScope_ReturnsBadRequest()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync("checkout-active-same-scope@example.com", [TryOutSpotRoles.Parent]);
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);
        var playerId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            dbContext.Subscriptions.Add(new AppSubscription
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                PlanType = TryOutSpotPlanCodes.PremiumPlayer,
                Status = "active",
                ScopeType = TryOutSpotSubscriptionScopeTypes.Player,
                ScopeId = playerId,
                StripeCustomerId = "cus_existing",
                StripeSubscriptionId = "sub_existing",
                StripePriceId = "price_premium_month",
                CurrentPeriodStart = now,
                CurrentPeriodEnd = now.AddMonths(1),
                Amount = 9.99m,
                Currency = "USD",
                BillingInterval = BillingIntervalCodes.Month,
                CreatedAt = now,
                UpdatedAt = now
            });
            await dbContext.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync(
            "/api/billing/checkout-session",
            new CreateCheckoutSessionRequest(
                TryOutSpotPlanCodes.PremiumPlayer,
                BillingIntervalCodes.Month,
                TryOutSpotSubscriptionScopeTypes.Player,
                playerId));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateCheckoutSession_WithTeamRepresentative_AllowsTeamAndEnterprisePlans()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync(
            "checkout-academy-director@example.com",
            [TryOutSpotRoles.TeamRepresentative]);
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        var teamResponse = await client.PostAsJsonAsync(
            "/api/billing/checkout-session",
            new CreateCheckoutSessionRequest(TryOutSpotPlanCodes.TeamProfessional, BillingIntervalCodes.Year));

        Assert.Equal(HttpStatusCode.OK, teamResponse.StatusCode);

        var enterpriseResponse = await client.PostAsJsonAsync(
            "/api/billing/checkout-session",
            new CreateCheckoutSessionRequest(TryOutSpotPlanCodes.EnterpriseOrganization, BillingIntervalCodes.Year));

        Assert.Equal(HttpStatusCode.OK, enterpriseResponse.StatusCode);
    }

    [Fact]
    public async Task CreateCheckoutSession_WithTeamRepresentative_AllowsTeamProfessionalAndEnterprisePlans()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync(
            "checkout-organization-admin@example.com",
            [TryOutSpotRoles.TeamRepresentative]);
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        var teamProfessionalResponse = await client.PostAsJsonAsync(
            "/api/billing/checkout-session",
            new CreateCheckoutSessionRequest(TryOutSpotPlanCodes.TeamProfessional, BillingIntervalCodes.Year));
        Assert.Equal(HttpStatusCode.OK, teamProfessionalResponse.StatusCode);

        var enterpriseResponse = await client.PostAsJsonAsync(
            "/api/billing/checkout-session",
            new CreateCheckoutSessionRequest(TryOutSpotPlanCodes.EnterpriseOrganization, BillingIntervalCodes.Year));
        Assert.Equal(HttpStatusCode.OK, enterpriseResponse.StatusCode);
    }

    [Fact]
    public async Task CreateCheckoutSession_WithTeamProfessionalMonthlyInterval_ReturnsBadRequest()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync(
            "checkout-team-pro-monthly-not-allowed@example.com",
            [TryOutSpotRoles.TeamRepresentative]);
        var client = await CreateAuthorizedClientAsync(factory, user.Email!);

        var response = await client.PostAsJsonAsync(
            "/api/billing/checkout-session",
            new CreateCheckoutSessionRequest(TryOutSpotPlanCodes.TeamProfessional, BillingIntervalCodes.Month));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task StripeSubscriptionSync_ActiveThenCanceledSubscriptionUpdatesEntitlements()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync("team-sync@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var now = DateTime.UtcNow;

        using var scope = factory.Services.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<IStripeSubscriptionSyncService>();
        await syncService.ApplyStripeSubscriptionAsync(
            new StripeSubscriptionSnapshot(
                "sub_team_pro",
                "cus_team_pro",
                user.Id,
                TryOutSpotPlanCodes.TeamProfessional,
                "price_team_professional_month",
                "active",
                now,
                now.AddMonths(1),
                null,
                79m,
                "usd",
                BillingIntervalCodes.Month,
                TryOutSpotSubscriptionScopeTypes.Account,
                null,
                false,
                null),
            CancellationToken.None);

        var entitlementService = scope.ServiceProvider.GetRequiredService<IEntitlementService>();
        var activeEntitlements = await entitlementService.GetEntitlementsAsync(user.Id, CancellationToken.None);

        Assert.NotNull(activeEntitlements);
        Assert.Contains(TryOutSpotFeatureCodes.AdvancedPlayerSearch, activeEntitlements.FeatureCodes);

        await syncService.ApplyStripeSubscriptionAsync(
            new StripeSubscriptionSnapshot(
                "sub_team_pro",
                "cus_team_pro",
                user.Id,
                TryOutSpotPlanCodes.TeamProfessional,
                "price_team_professional_month",
                "canceled",
                now,
                now.AddMonths(1),
                null,
                79m,
                "usd",
                BillingIntervalCodes.Month,
                TryOutSpotSubscriptionScopeTypes.Account,
                null,
                false,
                now),
            CancellationToken.None);

        var canceledEntitlements = await entitlementService.GetEntitlementsAsync(user.Id, CancellationToken.None);

        Assert.NotNull(canceledEntitlements);
        Assert.DoesNotContain(TryOutSpotFeatureCodes.AdvancedPlayerSearch, canceledEntitlements.FeatureCodes);
    }

    [Fact]
    public async Task StripeSubscriptionSync_EnterpriseCancellation_SoftDeletesTeamAndOrganizationData()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync("enterprise-cancel@example.com", [TryOutSpotRoles.TeamRepresentative]);
        var now = DateTime.UtcNow;
        Guid teamId;
        Guid organizationId;
        Guid opportunityId;

        using (var setupScope = factory.Services.CreateScope())
        {
            var dbContext = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var baseballSportId = await dbContext.Sports
                .Where(sport => sport.IsActive && sport.Name == "Baseball")
                .Select(sport => sport.Id)
                .SingleAsync();

            organizationId = Guid.NewGuid();
            teamId = Guid.NewGuid();
            opportunityId = Guid.NewGuid();

            dbContext.Organizations.Add(new Organization
            {
                Id = organizationId,
                Name = "Midamserv Organization",
                Email = "org@example.com",
                PhoneNumber = "620-555-3030",
                WebsiteUrl = "https://midamserv.test",
                SocialMediaLinks = "{\"facebook\":\"https://facebook.com/midamserv\"}",
                IsSearchable = true,
                IsContactInfoVisible = true,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
            dbContext.Teams.Add(new Team
            {
                Id = teamId,
                OrganizationId = organizationId,
                Name = "Midamserv Thunder Elite",
                Email = "team@example.com",
                PhoneNumber = "620-555-4040",
                WebsiteUrl = "https://team.midamserv.test",
                SocialMediaLinks = "{\"instagram\":\"https://instagram.com/midamserv\"}",
                IsSearchable = true,
                IsContactInfoVisible = true,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            });
            dbContext.UserTeamRoles.Add(new UserTeamRole
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                TeamId = teamId,
                Role = TryOutSpotRoles.TeamRepresentative,
                StartDate = now,
                IsActive = true,
                CreatedAt = now
            });
            dbContext.TeamSports.Add(new TeamSport
            {
                Id = Guid.NewGuid(),
                TeamId = teamId,
                SportId = baseballSportId,
                IsActive = true,
                CreatedAt = now
            });
            dbContext.Opportunities.Add(new Opportunity
            {
                Id = opportunityId,
                TeamId = teamId,
                SportId = baseballSportId,
                Type = "Tryout",
                Title = "Spring roster tryout",
                ContactEmail = "opportunity@example.com",
                ContactPhone = "620-555-5050",
                WebsiteUrl = "https://midamserv.test/tryout",
                IsPublished = true,
                IsActive = true,
                RegistrationRequired = true,
                RegistrationFee = 0m,
                CreatedAt = now,
                UpdatedAt = now
            });

            await dbContext.SaveChangesAsync();
        }

        using var scope = factory.Services.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<IStripeSubscriptionSyncService>();
        await syncService.ApplyStripeSubscriptionAsync(
            new StripeSubscriptionSnapshot(
                "sub_enterprise",
                "cus_enterprise",
                user.Id,
                TryOutSpotPlanCodes.EnterpriseOrganization,
                "price_enterprise_year",
                "active",
                now,
                now.AddYears(1),
                null,
                1999m,
                "usd",
                BillingIntervalCodes.Year,
                TryOutSpotSubscriptionScopeTypes.Account,
                null,
                true,
                null),
            CancellationToken.None);

        var verifyDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var team = await verifyDb.Teams.SingleAsync(currentTeam => currentTeam.Id == teamId);
        var organization = await verifyDb.Organizations.SingleAsync(currentOrganization => currentOrganization.Id == organizationId);
        var role = await verifyDb.UserTeamRoles.SingleAsync(currentRole => currentRole.TeamId == teamId && currentRole.UserId == user.Id);
        var teamSport = await verifyDb.TeamSports.SingleAsync(currentTeamSport => currentTeamSport.TeamId == teamId);
        var opportunity = await verifyDb.Opportunities.SingleAsync(currentOpportunity => currentOpportunity.Id == opportunityId);

        Assert.False(team.IsActive);
        Assert.False(team.IsSearchable);
        Assert.False(team.IsContactInfoVisible);
        Assert.Null(team.Email);
        Assert.Null(team.PhoneNumber);
        Assert.Null(team.WebsiteUrl);
        Assert.Null(team.SocialMediaLinks);

        Assert.False(organization.IsActive);
        Assert.False(organization.IsSearchable);
        Assert.False(organization.IsContactInfoVisible);
        Assert.Null(organization.Email);
        Assert.Null(organization.PhoneNumber);
        Assert.Null(organization.WebsiteUrl);
        Assert.Null(organization.SocialMediaLinks);

        Assert.False(role.IsActive);
        Assert.NotNull(role.EndDate);
        Assert.False(teamSport.IsActive);
        Assert.False(opportunity.IsActive);
        Assert.False(opportunity.IsPublished);
        Assert.Null(opportunity.ContactEmail);
        Assert.Null(opportunity.ContactPhone);
        Assert.Null(opportunity.WebsiteUrl);
    }

    private static TryOutSpotWebApplicationFactory CreateFactoryWithStripe()
    {
        return new TryOutSpotWebApplicationFactory(services =>
        {
            services.RemoveAll<IStripeBillingService>();
            services.AddSingleton<IStripeBillingService, TestStripeBillingService>();
            services.PostConfigure<StripeBillingOptions>(options =>
            {
                options.SecretKey = "stripe_secret_placeholder";
                options.WebhookSigningSecret = "stripe_webhook_placeholder";
                options.SuccessUrl = "https://example.test/billing/success?session_id={CHECKOUT_SESSION_ID}";
                options.CancelUrl = "https://example.test/billing/cancelled";
                options.PortalReturnUrl = "https://example.test/account/settings";
                options.Plans = new Dictionary<string, StripeBillingPlanPriceOptions>
                {
                    [TryOutSpotPlanCodes.PremiumPlayer] = new()
                    {
                        MonthlyPriceId = "price_premium_month",
                        AnnualPriceId = "price_premium_year"
                    },
                    [TryOutSpotPlanCodes.TeamBasic] = new()
                    {
                        MonthlyPriceId = "price_team_basic_month"
                    },
                    [TryOutSpotPlanCodes.TeamProfessional] = new()
                    {
                        MonthlyPriceId = string.Empty,
                        AnnualPriceId = "price_team_professional_year"
                    },
                    [TryOutSpotPlanCodes.EnterpriseOrganization] = new()
                    {
                        MonthlyPriceId = string.Empty,
                        AnnualPriceId = "price_enterprise_year"
                    }
                };
            });
        });
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
            "Bearer",
            token.AccessToken);
        return client;
    }

    private static async Task<HttpClient> CreateAdminClientAsync(TryOutSpotWebApplicationFactory factory)
    {
        var adminTokens = await factory.LoginAsPlatformAdminAsync();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            adminTokens.TokenType,
            adminTokens.AccessToken);
        return client;
    }

    private sealed class TestStripeBillingService : IStripeBillingService
    {
        public Task<StripeCheckoutSessionResult> CreateCheckoutSessionAsync(
            User user,
            string? existingStripeCustomerId,
            BillingPlanDefinition plan,
            string billingInterval,
            string stripePriceId,
            string scopeType,
            Guid? scopeId,
            string? idempotencyKey,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new StripeCheckoutSessionResult(
                "cs_test_checkout",
                "https://checkout.stripe.test/session",
                existingStripeCustomerId ?? "cus_test_checkout"));
        }

        public Task<StripeBillingPortalSessionResult> CreatePortalSessionAsync(
            string stripeCustomerId,
            string? idempotencyKey,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new StripeBillingPortalSessionResult("https://billing.stripe.test/session"));
        }

        public Task<StripeSubscriptionSnapshot?> ScheduleCancellationAtPeriodEndAsync(
            string stripeSubscriptionId,
            string? idempotencyKey,
            CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            return Task.FromResult<StripeSubscriptionSnapshot?>(new StripeSubscriptionSnapshot(
                stripeSubscriptionId,
                "cus_test_checkout",
                null,
                TryOutSpotPlanCodes.PremiumPlayer,
                "price_premium_month",
                "active",
                now,
                now.AddMonths(1),
                null,
                9.99m,
                "usd",
                BillingIntervalCodes.Month,
                TryOutSpotSubscriptionScopeTypes.Account,
                null,
                true,
                null));
        }

        public Task<Stripe.Subscription?> GetSubscriptionAsync(
            string stripeSubscriptionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<Stripe.Subscription?>(null);
        }

        public Event ConstructWebhookEvent(string payload, string signatureHeader)
        {
            throw new NotSupportedException("Webhook construction is not used by these tests.");
        }
    }
}

