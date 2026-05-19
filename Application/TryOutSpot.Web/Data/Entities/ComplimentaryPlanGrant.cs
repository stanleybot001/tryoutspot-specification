namespace TryOutSpot.Web.Data.Entities;

public partial class ComplimentaryPlanGrant
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string PlanType { get; set; } = null!;

    public string ScopeType { get; set; } = "account";

    public Guid? ScopeId { get; set; }

    public DateTime StartsAt { get; set; }

    public DateTime? EndsAt { get; set; }

    public string Source { get; set; } = "admin";

    public string? PromotionCode { get; set; }

    public string? Reason { get; set; }

    public Guid GrantedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public Guid? RevokedByUserId { get; set; }

    public string? RevokeReason { get; set; }
}
