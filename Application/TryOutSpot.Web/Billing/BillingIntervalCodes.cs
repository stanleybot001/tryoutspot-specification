namespace TryOutSpot.Web.Billing;

public static class BillingIntervalCodes
{
    public const string Month = "month";
    public const string Year = "year";

    public static string? Normalize(string? billingInterval)
    {
        if (string.IsNullOrWhiteSpace(billingInterval))
        {
            return Month;
        }

        var normalized = billingInterval.Trim().Replace("-", "_").Replace(" ", "_").ToLowerInvariant();
        return normalized switch
        {
            Month or "monthly" => Month,
            Year or "annual" or "annually" or "yearly" => Year,
            _ => null
        };
    }
}
