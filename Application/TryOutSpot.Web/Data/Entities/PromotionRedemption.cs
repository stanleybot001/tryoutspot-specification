namespace TryOutSpot.Web.Data.Entities;

public partial class PromotionRedemption
{
    public Guid Id { get; set; }

    public string PromotionCode { get; set; } = null!;

    public Guid UserId { get; set; }

    public string GrantedPlanCodes { get; set; } = null!;

    public DateTime RedeemedAt { get; set; }
}
