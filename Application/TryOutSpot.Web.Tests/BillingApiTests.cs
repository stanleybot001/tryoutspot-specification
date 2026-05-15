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

    [Theory]
    [InlineData(TryOutSpotRoles.Parent, TryOutSpotPlanCodes.TeamBasic)]
    [InlineData(TryOutSpotRoles.Coach, TryOutSpotPlanCodes.PremiumPlayer)]
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
            [playerSideRole, TryOutSpotRoles.Coach]);
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
    public async Task CreateCheckoutSession_WithAcademyDirector_AllowsTeamAndEnterprisePlans()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync(
            "checkout-academy-director@example.com",
            [TryOutSpotRoles.AcademyDirector]);
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
    public async Task CreateCheckoutSession_WithOrganizationAdmin_AllowsTeamProfessionalAndEnterprisePlans()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync(
            "checkout-organization-admin@example.com",
            [TryOutSpotRoles.OrganizationAdmin]);
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
            [TryOutSpotRoles.Coach]);
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
    public async Task StripeSubscriptionSync_OffseasonHold_LeavesListingSearchableButHidesContact()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync("offseason-hold@example.com", [TryOutSpotRoles.Coach]);
        var now = DateTime.UtcNow;
        Guid teamId;

        using (var setupScope = factory.Services.CreateScope())
        {
            var dbContext = setupScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var organization = new Organization
            {
                Id = Guid.NewGuid(),
                Name = "Midamserv Club",
                Email = "club@example.com",
                PhoneNumber = "620-555-1010",
                SocialMediaLinks = "{\"facebook\":\"https://facebook.com/midamserv\"}",
                IsSearchable = false,
                IsContactInfoVisible = true,
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now
            };

            teamId = Guid.NewGuid();
            dbContext.Organizations.Add(organization);
            dbContext.Teams.Add(new Team
            {
                Id = teamId,
                OrganizationId = organization.Id,
                Name = "Midamserv Thunder",
                Email = "coach@example.com",
                PhoneNumber = "620-555-2020",
                SocialMediaLinks = "{\"instagram\":\"https://instagram.com/midamserv\"}",
                IsSearchable = false,
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
                Role = TryOutSpotRoles.Coach,
                StartDate = now,
                IsActive = true,
                CreatedAt = now
            });

            await dbContext.SaveChangesAsync();
        }

        using var scope = factory.Services.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<IStripeSubscriptionSyncService>();
        await syncService.ApplyStripeSubscriptionAsync(
            new StripeSubscriptionSnapshot(
                "sub_team_hold",
                "cus_team_hold",
                user.Id,
                TryOutSpotPlanCodes.TeamOffseasonHold,
                "price_team_offseason_hold_month",
                "active",
                now,
                now.AddMonths(1),
                null,
                15.99m,
                "usd",
                BillingIntervalCodes.Month,
                TryOutSpotSubscriptionScopeTypes.Account,
                null,
                false,
                null),
            CancellationToken.None);

        var verifyDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var team = await verifyDb.Teams.SingleAsync(currentTeam => currentTeam.Id == teamId);
        var organizationAfterHold = await verifyDb.Organizations.SingleAsync(currentOrganization => currentOrganization.Id == team.OrganizationId);

        Assert.True(team.IsActive);
        Assert.True(team.IsSearchable);
        Assert.False(team.IsContactInfoVisible);
        Assert.True(organizationAfterHold.IsActive);
        Assert.True(organizationAfterHold.IsSearchable);
        Assert.False(organizationAfterHold.IsContactInfoVisible);
    }

    [Fact]
    public async Task StripeSubscriptionSync_EnterpriseCancellation_SoftDeletesTeamAndOrganizationData()
    {
        await using var factory = CreateFactoryWithStripe();
        var user = await factory.CreateUserAsync("enterprise-cancel@example.com", [TryOutSpotRoles.OrganizationAdmin]);
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
                Role = TryOutSpotRoles.OrganizationAdmin,
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
                    [TryOutSpotPlanCodes.TeamOffseasonHold] = new()
                    {
                        MonthlyPriceId = "price_team_offseason_hold_month"
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
