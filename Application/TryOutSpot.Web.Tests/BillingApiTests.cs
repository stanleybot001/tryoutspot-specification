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
        Assert.Equal("cus_test_checkout", subscription.StripeCustomerId);
        Assert.Equal("price_premium_month", subscription.StripePriceId);
        Assert.Equal(9.99m, subscription.Amount);

        var entitlementService = scope.ServiceProvider.GetRequiredService<IEntitlementService>();
        var entitlements = await entitlementService.GetEntitlementsAsync(user.Id, CancellationToken.None);

        Assert.NotNull(entitlements);
        Assert.DoesNotContain(TryOutSpotFeatureCodes.PriorityApplicationReview, entitlements.FeatureCodes);
    }

    [Fact]
    public async Task StripeSubscriptionSync_ActiveThenCanceledSubscriptionUpdatesEntitlements()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync("team-sync@example.com", [TryOutSpotRoles.Coach]);
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
                false,
                now),
            CancellationToken.None);

        var canceledEntitlements = await entitlementService.GetEntitlementsAsync(user.Id, CancellationToken.None);

        Assert.NotNull(canceledEntitlements);
        Assert.DoesNotContain(TryOutSpotFeatureCodes.AdvancedPlayerSearch, canceledEntitlements.FeatureCodes);
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
                        MonthlyPriceId = "price_team_professional_month",
                        AnnualPriceId = "price_team_professional_year"
                    },
                    [TryOutSpotPlanCodes.EnterpriseOrganization] = new()
                    {
                        MonthlyPriceId = "price_enterprise_month",
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

    private sealed class TestStripeBillingService : IStripeBillingService
    {
        public Task<StripeCheckoutSessionResult> CreateCheckoutSessionAsync(
            User user,
            string? existingStripeCustomerId,
            BillingPlanDefinition plan,
            string billingInterval,
            string stripePriceId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new StripeCheckoutSessionResult(
                "cs_test_checkout",
                "https://checkout.stripe.test/session",
                existingStripeCustomerId ?? "cus_test_checkout"));
        }

        public Task<StripeBillingPortalSessionResult> CreatePortalSessionAsync(
            string stripeCustomerId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new StripeBillingPortalSessionResult("https://billing.stripe.test/session"));
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
