namespace TryOutSpot.Web.Billing;

public static class TryOutSpotPromotionCodes
{
    public const string AdminComplimentaryGrantSource = "admin";
    public const string LaunchPromotionGrantSource = "promotion";
    public const string LaunchFirst1000TwoMonths = "launch_first_1000_two_months";
    public const string LaunchFounderOfferName = "Launch founder offer";
    public const int LaunchFirst1000GrantMonths = 2;
    public const int LaunchFirst1000MaxRedemptions = 1000;

    public static readonly Guid LaunchFounderOfferCampaignId = Guid.Parse("58c9ca90-6c60-4dde-b0f7-3d8b6a70ce0c");
}
