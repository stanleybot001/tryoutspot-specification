namespace TryOutSpot.Web.Billing;

public sealed record BillingFeatureDefinition(
    string Code,
    string Name,
    string Description,
    bool IsPaidFeature);
