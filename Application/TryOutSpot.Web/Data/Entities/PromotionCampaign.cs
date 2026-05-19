namespace TryOutSpot.Web.Data.Entities;

public partial class PromotionCampaign
{
    public Guid Id { get; set; }

    public string Code { get; set; } = null!;

    public string Name { get; set; } = null!;

    public int MaxRedemptions { get; set; }

    public int GrantMonths { get; set; }

    public bool IsActive { get; set; }

    public bool IsEnabled { get; set; } = true;

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
