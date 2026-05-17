using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Security.Claims;
using Stripe;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Models.Billing;
using TryOutSpot.Web.Security;
using TryOutSpot.Web.Services;
using AppSubscription = TryOutSpot.Web.Data.Entities.Subscription;
using StripeCheckoutSession = Stripe.Checkout.Session;
using StripeInvoice = Stripe.Invoice;
using StripeSubscription = Stripe.Subscription;

namespace TryOutSpot.Web.Controllers;

/// <summary>
/// Billing plans, feature catalog, and entitlement reference endpoints.
/// </summary>
[ApiController]
[Tags("Billing")]
[Produces("application/json")]
[Route("api/billing")]
public sealed class BillingApiController(
    AppDbContext dbContext,
    UserManager<User> userManager,
    IStripeBillingService stripeBillingService,
    IStripeSubscriptionSyncService stripeSubscriptionSyncService,
    IAccountTypeChangeWorkflowService accountTypeChangeWorkflowService,
    IOptions<StripeBillingOptions> stripeOptions,
    ILogger<BillingApiController> logger) : ControllerBase
{
    private readonly StripeBillingOptions stripeBillingOptions = stripeOptions.Value;

    /// <summary>
    /// Lists the public billing plans and included feature codes.
    /// </summary>
    /// <response code="200">Returns the current billing plan catalog.</response>
    [HttpGet("plans")]
    [ProducesResponseType<BillingPlanResponse[]>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyCollection<BillingPlanResponse>> GetPlans()
    {
        var plans = TryOutSpotBillingCatalog.Plans
            .Select(plan => new BillingPlanResponse(
                plan.Code,
                plan.Name,
                plan.Audience,
                plan.Description,
                plan.MonthlyAmount,
                plan.AnnualAmount,
                plan.Currency,
                plan.TrialDays,
                plan.RequiresStripeSubscription,
                plan.IncludedFeatureCodes)
            {
                Prices = BuildPriceResponses(plan)
            })
            .ToArray();

        return Ok(plans);
    }

    /// <summary>
    /// Lists all known feature codes used for free and paid entitlement checks.
    /// </summary>
    /// <response code="200">Returns the current billing feature catalog.</response>
    [HttpGet("features")]
    [ProducesResponseType<BillingFeatureResponse[]>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyCollection<BillingFeatureResponse>> GetFeatures()
    {
        var features = TryOutSpotBillingCatalog.Features
            .Select(feature => new BillingFeatureResponse(
                feature.Code,
                feature.Name,
                feature.Description,
                feature.IsPaidFeature))
            .ToArray();

        return Ok(features);
    }

    /// <summary>
    /// Returns the authenticated user's current billing status.
    /// </summary>
    /// <response code="200">Returns current billing status.</response>
    /// <response code="401">The bearer token or web session is missing or invalid.</response>
    [Authorize(Policy = TryOutSpotAuthorizationPolicies.ConfirmedEmail)]
    [HttpGet("me")]
    [ProducesResponseType<CurrentBillingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentBillingResponse>> GetCurrentBilling(
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserWithSubscriptionsAsync(cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var subscriptions = user.Subscriptions
            .OrderByDescending(subscription => TryOutSpotBillingCatalog.IsEntitlingSubscriptionStatus(subscription.Status))
            .ThenByDescending(subscription => subscription.UpdatedAt)
            .Select(ToCurrentBillingSubscriptionResponse)
            .ToArray();
        var primarySubscription = subscriptions.FirstOrDefault(subscription => subscription.HasActiveEntitlement)
            ?? subscriptions.FirstOrDefault();

        return Ok(new CurrentBillingResponse(
            primarySubscription?.PlanCode,
            primarySubscription?.PlanName,
            primarySubscription?.Status,
            primarySubscription?.HasActiveEntitlement ?? false,
            primarySubscription?.BillingInterval,
            primarySubscription?.Amount,
            primarySubscription?.Currency,
            primarySubscription?.CurrentPeriodEnd,
            primarySubscription?.CancelAtPeriodEnd ?? false)
        {
            Subscriptions = subscriptions
        });
    }

    /// <summary>
    /// Creates a Stripe Checkout session for a paid subscription plan.
    /// </summary>
    /// <remarks>
    /// Checkout starts payment collection only. Access is granted by Stripe webhook events.
    /// </remarks>
    /// <response code="200">Returns the Stripe Checkout URL.</response>
    /// <response code="400">The request is invalid or Stripe is not configured for this plan.</response>
    /// <response code="401">The bearer token or web session is missing or invalid.</response>
    [Authorize(Policy = TryOutSpotAuthorizationPolicies.ConfirmedEmail)]
    [HttpPost("checkout-session")]
    [ProducesResponseType<CheckoutSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CheckoutSessionResponse>> CreateCheckoutSession(
        CreateCheckoutSessionRequest request,
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserWithSubscriptionsAsync(cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        if (!stripeBillingOptions.IsConfigured)
        {
            ModelState.AddModelError(nameof(StripeBillingOptions.SecretKey), "Stripe is not configured.");
            return ValidationProblem(ModelState);
        }

        var plan = TryOutSpotBillingCatalog.GetPlan(request.PlanCode);
        if (plan is null)
        {
            ModelState.AddModelError(nameof(request.PlanCode), $"'{request.PlanCode}' is not a supported subscription plan.");
            return ValidationProblem(ModelState);
        }

        if (!plan.RequiresStripeSubscription)
        {
            ModelState.AddModelError(nameof(request.PlanCode), "This plan does not require Stripe checkout.");
            return ValidationProblem(ModelState);
        }

        var accountTypes = await userManager.GetRolesAsync(user);
        if (!TryOutSpotBillingCatalog.IsPlanEligibleForAccountTypes(plan.Code, accountTypes))
        {
            ModelState.AddModelError(
                nameof(request.PlanCode),
                "This plan is not available for the selected account types.");
            return ValidationProblem(ModelState);
        }

        var scope = ValidateSubscriptionScope(plan.Code, request.ScopeType, request.ScopeId);
        if (scope is null)
        {
            return ValidationProblem(ModelState);
        }

        var existingSubscription = FindSubscriptionForPlanScope(
            user.Subscriptions,
            plan.Code,
            scope.ScopeType,
            scope.ScopeId);
        if (existingSubscription is not null
            && TryOutSpotBillingCatalog.IsEntitlingSubscriptionStatus(existingSubscription.Status))
        {
            ModelState.AddModelError(
                nameof(request.PlanCode),
                "This subscription is already active for the selected scope.");
            return ValidationProblem(ModelState);
        }

        var billingInterval = BillingIntervalCodes.Normalize(request.BillingInterval);
        if (billingInterval is null)
        {
            ModelState.AddModelError(nameof(request.BillingInterval), "Choose monthly or annual billing.");
            return ValidationProblem(ModelState);
        }

        if (!TryOutSpotBillingCatalog.IsBillingIntervalSupported(plan.Code, billingInterval))
        {
            var message = TryOutSpotBillingCatalog.RequiresAnnualCommitment(plan.Code)
                ? "This plan requires annual billing."
                : "This plan does not support the requested billing interval.";
            ModelState.AddModelError(nameof(request.BillingInterval), message);
            return ValidationProblem(ModelState);
        }

        var amount = GetAmountForInterval(plan, billingInterval);
        if (amount is null)
        {
            ModelState.AddModelError(nameof(request.BillingInterval), "This plan does not support the requested billing interval.");
            return ValidationProblem(ModelState);
        }

        var stripePriceId = stripeBillingOptions.GetPriceId(plan.Code, billingInterval);
        if (stripePriceId is null)
        {
            ModelState.AddModelError(nameof(request.PlanCode), "Stripe price is not configured for this plan and billing interval.");
            return ValidationProblem(ModelState);
        }

        var checkoutSession = await stripeBillingService.CreateCheckoutSessionAsync(
            user,
            ResolveStripeCustomerId(user.Subscriptions),
            plan,
            billingInterval,
            stripePriceId,
            scope.ScopeType,
            scope.ScopeId,
            BuildCheckoutIdempotencyKey(user.Id, plan.Code, billingInterval, scope.ScopeType, scope.ScopeId),
            cancellationToken);

        var subscription = await RecordCheckoutStartedAsync(
            user,
            plan,
            billingInterval,
            amount.Value,
            stripePriceId,
            checkoutSession.StripeCustomerId,
            scope.ScopeType,
            scope.ScopeId,
            cancellationToken);

        return Ok(new CheckoutSessionResponse(
            checkoutSession.SessionId,
            checkoutSession.Url,
            plan.Code,
            billingInterval)
        {
            SubscriptionId = subscription.Id,
            ScopeType = subscription.ScopeType,
            ScopeId = subscription.ScopeId
        });
    }

    /// <summary>
    /// Creates a Stripe billing portal session for the authenticated user.
    /// </summary>
    /// <response code="200">Returns the Stripe billing portal URL.</response>
    /// <response code="400">The user does not have a Stripe customer yet.</response>
    /// <response code="401">The bearer token or web session is missing or invalid.</response>
    [Authorize(Policy = TryOutSpotAuthorizationPolicies.ConfirmedEmail)]
    [HttpPost("customer-portal-session")]
    [ProducesResponseType<BillingPortalSessionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<BillingPortalSessionResponse>> CreateCustomerPortalSession(
        CancellationToken cancellationToken)
    {
        var user = await GetCurrentUserWithSubscriptionsAsync(cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var stripeCustomerId = ResolveStripeCustomerId(user.Subscriptions);
        if (string.IsNullOrWhiteSpace(stripeCustomerId))
        {
            ModelState.AddModelError("subscription", "No Stripe customer is linked to this account yet.");
            return ValidationProblem(ModelState);
        }

        var portalSession = await stripeBillingService.CreatePortalSessionAsync(
            stripeCustomerId,
            BuildCustomerPortalIdempotencyKey(user.Id),
            cancellationToken);

        return Ok(new BillingPortalSessionResponse(portalSession.Url));
    }

    /// <summary>
    /// Receives Stripe webhook events and updates local subscription state.
    /// </summary>
    /// <response code="200">The webhook event was accepted.</response>
    /// <response code="400">The webhook payload or signature is invalid.</response>
    [AllowAnonymous]
    [HttpPost("stripe/webhook")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> StripeWebhook(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        var signatureHeader = Request.Headers["Stripe-Signature"].ToString();
        if (string.IsNullOrWhiteSpace(signatureHeader))
        {
            return BadRequest();
        }

        Stripe.Event stripeEvent;
        try
        {
            stripeEvent = stripeBillingService.ConstructWebhookEvent(payload, signatureHeader);
        }
        catch (Exception exception) when (exception is StripeException or InvalidOperationException or ArgumentException)
        {
            logger.LogWarning(exception, "Stripe webhook signature or payload validation failed.");
            return BadRequest();
        }

        if (await IsStripeWebhookEventProcessedAsync(stripeEvent.Id, cancellationToken))
        {
            logger.LogInformation(
                "Stripe webhook event {StripeEventId} has already been processed. Skipping duplicate delivery.",
                stripeEvent.Id);
            return Ok();
        }

        await HandleStripeEventAsync(stripeEvent, cancellationToken);
        await RecordProcessedStripeWebhookEventAsync(stripeEvent, cancellationToken);
        return Ok();
    }

    private IReadOnlyCollection<BillingPlanPriceResponse> BuildPriceResponses(BillingPlanDefinition plan)
    {
        var prices = new List<BillingPlanPriceResponse>();
        var supportsMonthly = TryOutSpotBillingCatalog.IsBillingIntervalSupported(plan.Code, BillingIntervalCodes.Month);
        var supportsAnnual = TryOutSpotBillingCatalog.IsBillingIntervalSupported(plan.Code, BillingIntervalCodes.Year);

        if (supportsMonthly && (!plan.RequiresStripeSubscription || plan.MonthlyAmount > 0m))
        {
            prices.Add(new BillingPlanPriceResponse(
                BillingIntervalCodes.Month,
                plan.MonthlyAmount,
                plan.Currency,
                !plan.RequiresStripeSubscription || stripeBillingOptions.GetPriceId(plan.Code, BillingIntervalCodes.Month) is not null));
        }

        if (supportsAnnual && plan.AnnualAmount is { } annualAmount)
        {
            prices.Add(new BillingPlanPriceResponse(
                BillingIntervalCodes.Year,
                annualAmount,
                plan.Currency,
                stripeBillingOptions.GetPriceId(plan.Code, BillingIntervalCodes.Year) is not null));
        }

        return prices;
    }

    private async Task HandleStripeEventAsync(
        Stripe.Event stripeEvent,
        CancellationToken cancellationToken)
    {
        switch (stripeEvent.Type)
        {
            case "checkout.session.completed":
                await HandleCheckoutSessionCompletedAsync(stripeEvent, cancellationToken);
                break;
            case "customer.subscription.created":
            case "customer.subscription.updated":
            case "customer.subscription.deleted":
                await HandleSubscriptionEventAsync(stripeEvent, cancellationToken);
                break;
            default:
                logger.LogInformation("Stripe webhook event {StripeEventType} was ignored.", stripeEvent.Type);
                break;
        }
    }

    private async Task HandleCheckoutSessionCompletedAsync(
        Stripe.Event stripeEvent,
        CancellationToken cancellationToken)
    {
        if (stripeEvent.Data.Object is not StripeCheckoutSession session
            || string.IsNullOrWhiteSpace(session.SubscriptionId))
        {
            logger.LogWarning("Stripe checkout session event {StripeEventId} did not contain a subscription id.", stripeEvent.Id);
            return;
        }

        var subscription = await stripeBillingService.GetSubscriptionAsync(session.SubscriptionId, cancellationToken);
        if (subscription is null)
        {
            logger.LogWarning("Stripe subscription {StripeSubscriptionId} was not found after checkout completion.", session.SubscriptionId);
            return;
        }

        var synced = await stripeSubscriptionSyncService.ApplyStripeSubscriptionAsync(
            StripeSubscriptionSnapshotFactory.FromStripeSubscription(subscription),
            cancellationToken);
        if (synced is not null)
        {
            await accountTypeChangeWorkflowService.ReconcilePendingChangesForUserAsync(synced.UserId, cancellationToken);
        }
    }

    private async Task HandleSubscriptionEventAsync(
        Stripe.Event stripeEvent,
        CancellationToken cancellationToken)
    {
        if (stripeEvent.Data.Object is not StripeSubscription subscription)
        {
            logger.LogWarning("Stripe subscription event {StripeEventId} could not be parsed.", stripeEvent.Id);
            return;
        }

        var synced = await stripeSubscriptionSyncService.ApplyStripeSubscriptionAsync(
            StripeSubscriptionSnapshotFactory.FromStripeSubscription(subscription),
            cancellationToken);
        if (synced is not null)
        {
            await accountTypeChangeWorkflowService.ReconcilePendingChangesForUserAsync(synced.UserId, cancellationToken);
        }
    }

    private async Task<User?> GetCurrentUserWithSubscriptionsAsync(CancellationToken cancellationToken)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return null;
        }

        return await dbContext.Users
            .Include(user => user.Subscriptions)
            .SingleOrDefaultAsync(user => user.Id == userId && user.IsActive, cancellationToken);
    }

    private async Task<AppSubscription> RecordCheckoutStartedAsync(
        User user,
        BillingPlanDefinition plan,
        string billingInterval,
        decimal amount,
        string stripePriceId,
        string stripeCustomerId,
        string scopeType,
        Guid? scopeId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var subscription = FindSubscriptionForPlanScope(user.Subscriptions, plan.Code, scopeType, scopeId);
        if (subscription is null)
        {
            subscription = new AppSubscription
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                PlanType = plan.Code,
                Status = "checkout_started",
                Currency = plan.Currency,
                BillingInterval = billingInterval,
                ScopeType = scopeType,
                ScopeId = scopeId,
                CreatedAt = now
            };
            dbContext.Subscriptions.Add(subscription);
            user.Subscriptions.Add(subscription);
        }

        if (!TryOutSpotBillingCatalog.IsEntitlingSubscriptionStatus(subscription.Status))
        {
            subscription.PlanType = plan.Code;
            subscription.Status = "checkout_started";
            subscription.ScopeType = scopeType;
            subscription.ScopeId = scopeId;
            subscription.Amount = amount;
            subscription.Currency = plan.Currency;
            subscription.BillingInterval = billingInterval;
            subscription.StripePriceId = stripePriceId;
            subscription.CancelAtPeriodEnd = false;
            subscription.CancelledAt = null;
            subscription.IsElite = string.Equals(plan.Code, TryOutSpotPlanCodes.PremiumPlayer, StringComparison.Ordinal);
        }

        subscription.StripeCustomerId = stripeCustomerId;
        subscription.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return subscription;
    }

    private SubscriptionScope? ValidateSubscriptionScope(string planCode, string? requestedScopeType, Guid? scopeId)
    {
        var scopeType = TryOutSpotSubscriptionScopeTypes.Normalize(requestedScopeType);
        if (scopeType is null)
        {
            ModelState.AddModelError(nameof(CreateCheckoutSessionRequest.ScopeType), "Choose account, player, team, or organization scope.");
            return null;
        }

        if (!TryOutSpotBillingCatalog.IsSubscriptionScopeEligibleForPlan(planCode, scopeType))
        {
            ModelState.AddModelError(nameof(CreateCheckoutSessionRequest.ScopeType), "This scope is not available for the selected plan.");
            return null;
        }

        if (scopeType == TryOutSpotSubscriptionScopeTypes.Account && scopeId is not null)
        {
            ModelState.AddModelError(nameof(CreateCheckoutSessionRequest.ScopeId), "Account-scoped subscriptions should not include a scope id.");
            return null;
        }

        if (scopeType != TryOutSpotSubscriptionScopeTypes.Account && scopeId is null)
        {
            ModelState.AddModelError(nameof(CreateCheckoutSessionRequest.ScopeId), "Choose the player, team, or organization this subscription applies to.");
            return null;
        }

        return new SubscriptionScope(scopeType, scopeId);
    }

    private static AppSubscription? FindSubscriptionForPlanScope(
        IEnumerable<AppSubscription> subscriptions,
        string planCode,
        string scopeType,
        Guid? scopeId)
    {
        return subscriptions
            .OrderByDescending(subscription => subscription.UpdatedAt)
            .FirstOrDefault(subscription =>
                string.Equals(subscription.PlanType, planCode, StringComparison.Ordinal)
                && string.Equals(subscription.ScopeType, scopeType, StringComparison.Ordinal)
                && subscription.ScopeId == scopeId);
    }

    private static string? ResolveStripeCustomerId(IEnumerable<AppSubscription> subscriptions)
    {
        return subscriptions
            .OrderByDescending(subscription => TryOutSpotBillingCatalog.IsEntitlingSubscriptionStatus(subscription.Status))
            .ThenByDescending(subscription => subscription.UpdatedAt)
            .Select(subscription => subscription.StripeCustomerId)
            .FirstOrDefault(stripeCustomerId => !string.IsNullOrWhiteSpace(stripeCustomerId));
    }

    private static CurrentBillingSubscriptionResponse ToCurrentBillingSubscriptionResponse(AppSubscription subscription)
    {
        var planCode = TryOutSpotBillingCatalog.NormalizePlanCode(subscription.PlanType)
            ?? (subscription.IsElite ? TryOutSpotPlanCodes.PremiumPlayer : subscription.PlanType);
        var plan = TryOutSpotBillingCatalog.GetPlan(planCode);
        var isEntitling = TryOutSpotBillingCatalog.IsEntitlingSubscriptionStatus(subscription.Status);

        return new CurrentBillingSubscriptionResponse(
            subscription.Id,
            planCode,
            plan?.Name ?? subscription.PlanType,
            subscription.Status,
            plan is not null && isEntitling,
            subscription.BillingInterval,
            subscription.Amount,
            subscription.Currency,
            subscription.CurrentPeriodEnd,
            subscription.CancelAtPeriodEnd,
            subscription.ScopeType,
            subscription.ScopeId);
    }

    private static decimal? GetAmountForInterval(BillingPlanDefinition plan, string billingInterval)
    {
        if (!TryOutSpotBillingCatalog.IsBillingIntervalSupported(plan.Code, billingInterval))
        {
            return null;
        }

        return billingInterval switch
        {
            BillingIntervalCodes.Month => plan.MonthlyAmount,
            BillingIntervalCodes.Year => plan.AnnualAmount,
            _ => null
        };
    }

    private async Task<bool> IsStripeWebhookEventProcessedAsync(
        string stripeEventId,
        CancellationToken cancellationToken)
    {
        return await dbContext.StripeWebhookEvents
            .AsNoTracking()
            .AnyAsync(webhookEvent => webhookEvent.StripeEventId == stripeEventId, cancellationToken);
    }

    private async Task RecordProcessedStripeWebhookEventAsync(
        Stripe.Event stripeEvent,
        CancellationToken cancellationToken)
    {
        dbContext.StripeWebhookEvents.Add(new StripeWebhookEvent
        {
            Id = Guid.NewGuid(),
            StripeEventId = stripeEvent.Id,
            EventType = stripeEvent.Type,
            StripeObjectId = TryGetStripeObjectId(stripeEvent),
            ProcessedAt = DateTime.UtcNow
        });

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateStripeWebhookEvent(exception))
        {
            logger.LogInformation(
                "Stripe webhook event {StripeEventId} was already recorded by another request.",
                stripeEvent.Id);
        }
    }

    private static bool IsDuplicateStripeWebhookEvent(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException postgresException
            && string.Equals(postgresException.SqlState, PostgresErrorCodes.UniqueViolation, StringComparison.Ordinal);
    }

    private static string? TryGetStripeObjectId(Stripe.Event stripeEvent)
    {
        return stripeEvent.Data.Object switch
        {
            StripeCheckoutSession checkoutSession when !string.IsNullOrWhiteSpace(checkoutSession.Id) => checkoutSession.Id,
            StripeSubscription subscription when !string.IsNullOrWhiteSpace(subscription.Id) => subscription.Id,
            StripeInvoice invoice when !string.IsNullOrWhiteSpace(invoice.Id) => invoice.Id,
            _ => null
        };
    }

    private static string BuildCheckoutIdempotencyKey(
        Guid userId,
        string planCode,
        string billingInterval,
        string scopeType,
        Guid? scopeId)
    {
        var normalizedScopeId = scopeId?.ToString("N") ?? "account";
        var key = $"checkout:{userId:N}:{planCode}:{billingInterval}:{scopeType}:{normalizedScopeId}";
        return key.Length <= 255 ? key : key[..255];
    }

    private static string BuildCustomerPortalIdempotencyKey(Guid userId)
    {
        var key = $"portal:{userId:N}:{DateTime.UtcNow:yyyyMMddHHmm}";
        return key.Length <= 255 ? key : key[..255];
    }

    private sealed record SubscriptionScope(string ScopeType, Guid? ScopeId);
}
