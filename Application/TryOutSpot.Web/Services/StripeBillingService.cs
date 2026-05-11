using Microsoft.Extensions.Options;
using Stripe;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Data.Entities;
using BillingPortalSessionCreateOptions = Stripe.BillingPortal.SessionCreateOptions;
using BillingPortalSessionService = Stripe.BillingPortal.SessionService;
using CheckoutSessionCreateOptions = Stripe.Checkout.SessionCreateOptions;
using CheckoutSessionLineItemOptions = Stripe.Checkout.SessionLineItemOptions;
using CheckoutSessionService = Stripe.Checkout.SessionService;
using CheckoutSessionSubscriptionDataOptions = Stripe.Checkout.SessionSubscriptionDataOptions;

namespace TryOutSpot.Web.Services;

public interface IStripeBillingService
{
    Task<StripeCheckoutSessionResult> CreateCheckoutSessionAsync(
        User user,
        string? existingStripeCustomerId,
        BillingPlanDefinition plan,
        string billingInterval,
        string stripePriceId,
        CancellationToken cancellationToken);

    Task<StripeBillingPortalSessionResult> CreatePortalSessionAsync(
        string stripeCustomerId,
        CancellationToken cancellationToken);

    Task<Stripe.Subscription?> GetSubscriptionAsync(string stripeSubscriptionId, CancellationToken cancellationToken);

    Event ConstructWebhookEvent(string payload, string signatureHeader);
}

public sealed record StripeCheckoutSessionResult(
    string SessionId,
    string Url,
    string StripeCustomerId);

public sealed record StripeBillingPortalSessionResult(string Url);

public sealed class StripeBillingService(IOptions<StripeBillingOptions> options) : IStripeBillingService
{
    private const long WebhookSignatureToleranceSeconds = 300;

    private readonly StripeBillingOptions stripeOptions = options.Value;

    public async Task<StripeCheckoutSessionResult> CreateCheckoutSessionAsync(
        User user,
        string? existingStripeCustomerId,
        BillingPlanDefinition plan,
        string billingInterval,
        string stripePriceId,
        CancellationToken cancellationToken)
    {
        EnsureStripeApiIsConfigured();

        var stripeCustomerId = await GetOrCreateCustomerAsync(user, existingStripeCustomerId, cancellationToken);
        var metadata = BuildMetadata(user.Id, plan.Code, billingInterval);
        var subscriptionData = new CheckoutSessionSubscriptionDataOptions
        {
            Metadata = metadata
        };
        if (plan.TrialDays is > 0)
        {
            subscriptionData.TrialPeriodDays = plan.TrialDays.Value;
        }

        var createOptions = new CheckoutSessionCreateOptions
        {
            Mode = "subscription",
            Customer = stripeCustomerId,
            ClientReferenceId = user.Id.ToString(),
            SuccessUrl = stripeOptions.SuccessUrl,
            CancelUrl = stripeOptions.CancelUrl,
            Metadata = metadata,
            SubscriptionData = subscriptionData,
            LineItems =
            [
                new CheckoutSessionLineItemOptions
                {
                    Price = stripePriceId,
                    Quantity = 1
                }
            ]
        };

        var sessionService = new CheckoutSessionService();
        var session = await sessionService.CreateAsync(
            createOptions,
            BuildRequestOptions(),
            cancellationToken);

        if (string.IsNullOrWhiteSpace(session.Url))
        {
            throw new InvalidOperationException("Stripe did not return a Checkout URL.");
        }

        return new StripeCheckoutSessionResult(session.Id, session.Url, stripeCustomerId);
    }

    public async Task<StripeBillingPortalSessionResult> CreatePortalSessionAsync(
        string stripeCustomerId,
        CancellationToken cancellationToken)
    {
        EnsureStripeApiIsConfigured();

        var portalService = new BillingPortalSessionService();
        var session = await portalService.CreateAsync(
            new BillingPortalSessionCreateOptions
            {
                Customer = stripeCustomerId,
                ReturnUrl = stripeOptions.PortalReturnUrl
            },
            BuildRequestOptions(),
            cancellationToken);

        if (string.IsNullOrWhiteSpace(session.Url))
        {
            throw new InvalidOperationException("Stripe did not return a billing portal URL.");
        }

        return new StripeBillingPortalSessionResult(session.Url);
    }

    public async Task<Stripe.Subscription?> GetSubscriptionAsync(
        string stripeSubscriptionId,
        CancellationToken cancellationToken)
    {
        EnsureStripeApiIsConfigured();

        var subscriptionService = new SubscriptionService();
        return await subscriptionService.GetAsync(
            stripeSubscriptionId,
            new SubscriptionGetOptions
            {
                Expand = ["items.data.price"]
            },
            BuildRequestOptions(),
            cancellationToken);
    }

    public Event ConstructWebhookEvent(string payload, string signatureHeader)
    {
        if (!stripeOptions.WebhooksAreConfigured)
        {
            throw new InvalidOperationException("Stripe webhook signing secret is not configured.");
        }

        return EventUtility.ConstructEvent(
            payload,
            signatureHeader,
            stripeOptions.WebhookSigningSecret,
            WebhookSignatureToleranceSeconds,
            throwOnApiVersionMismatch: false);
    }

    private async Task<string> GetOrCreateCustomerAsync(
        User user,
        string? existingStripeCustomerId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(existingStripeCustomerId))
        {
            return existingStripeCustomerId;
        }

        var customerService = new CustomerService();
        var customer = await customerService.CreateAsync(
            new CustomerCreateOptions
            {
                Email = user.Email,
                Name = $"{user.FirstName} {user.LastName}".Trim(),
                Metadata = BuildMetadata(user.Id, null, null)
            },
            BuildRequestOptions(),
            cancellationToken);

        return customer.Id;
    }

    private static Dictionary<string, string> BuildMetadata(Guid userId, string? planCode, string? billingInterval)
    {
        var metadata = new Dictionary<string, string>
        {
            [StripeBillingMetadataKeys.UserId] = userId.ToString()
        };

        if (!string.IsNullOrWhiteSpace(planCode))
        {
            metadata[StripeBillingMetadataKeys.PlanCode] = planCode;
        }

        if (!string.IsNullOrWhiteSpace(billingInterval))
        {
            metadata[StripeBillingMetadataKeys.BillingInterval] = billingInterval;
        }

        return metadata;
    }

    private RequestOptions BuildRequestOptions()
    {
        return new RequestOptions { ApiKey = stripeOptions.SecretKey };
    }

    private void EnsureStripeApiIsConfigured()
    {
        if (!stripeOptions.IsConfigured)
        {
            throw new InvalidOperationException("Stripe secret key is not configured.");
        }
    }
}
