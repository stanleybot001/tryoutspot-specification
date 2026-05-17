namespace TryOutSpot.Web.Data.Entities;

public sealed class PendingAccountTypeChange
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string TargetRolesJson { get; set; } = "[]";

    public DateTime RequestedAt { get; set; }

    public DateTime? ApplyAfterUtc { get; set; }

    public string Status { get; set; } = "pending";

    public string? Notes { get; set; }

    public DateTime? ProcessedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
