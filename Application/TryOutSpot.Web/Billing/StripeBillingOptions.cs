namespace TryOutSpot.Web.Billing;

public sealed class StripeBillingOptions
{
    public const string SectionName = "Stripe";

    public string SecretKey { get; set; } = string.Empty;

    public string WebhookSigningSecret { get; set; } = string.Empty;

    public string SuccessUrl { get; set; } =
        "https://tryoutspot.com/account/settings?billing=success&session_id={CHECKOUT_SESSION_ID}";

    public string CancelUrl { get; set; } =
        "https://tryoutspot.com/account/settings?billing=cancelled";

    public string PortalReturnUrl { get; set; } =
        "https://tryoutspot.com/account/settings";

    public Dictionary<string, StripeBillingPlanPriceOptions> Plans { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsConfigured => HasConfiguredValue(SecretKey);

    public bool WebhooksAreConfigured => HasConfiguredValue(WebhookSigningSecret);

    public StripeBillingPlanPriceOptions? GetPlanOptions(string planCode)
    {
        if (Plans.TryGetValue(planCode, out var planOptions))
        {
            return planOptions;
        }

        var normalizedPlanCode = TryOutSpotBillingCatalog.NormalizePlanCode(planCode);
        if (normalizedPlanCode is not null && Plans.TryGetValue(normalizedPlanCode, out planOptions))
        {
            return planOptions;
        }

        return Plans
            .FirstOrDefault(plan => TryOutSpotBillingCatalog.NormalizePlanCode(plan.Key) == normalizedPlanCode)
            .Value;
    }

    public string? GetPriceId(string planCode, string billingInterval)
    {
        var planOptions = GetPlanOptions(planCode);
        if (planOptions is null)
        {
            return null;
        }

        return billingInterval switch
        {
            BillingIntervalCodes.Month => NormalizeConfiguredValue(planOptions.MonthlyPriceId),
            BillingIntervalCodes.Year => NormalizeConfiguredValue(planOptions.AnnualPriceId),
            _ => null
        };
    }

    public string? FindPlanCodeForPrice(string? priceId)
    {
        if (string.IsNullOrWhiteSpace(priceId))
        {
            return null;
        }

        foreach (var (planCode, planOptions) in Plans)
        {
            if (string.Equals(planOptions.MonthlyPriceId, priceId, StringComparison.Ordinal)
                || string.Equals(planOptions.AnnualPriceId, priceId, StringComparison.Ordinal))
            {
                return TryOutSpotBillingCatalog.NormalizePlanCode(planCode);
            }
        }

        return null;
    }

    private static string? NormalizeConfiguredValue(string? value)
    {
        return HasConfiguredValue(value) ? value!.Trim() : null;
    }

    private static bool HasConfiguredValue(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !value.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("PUT_", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class StripeBillingPlanPriceOptions
{
    public string MonthlyPriceId { get; set; } = string.Empty;

    public string AnnualPriceId { get; set; } = string.Empty;
}
