namespace TryOutSpot.Web.Data.Entities;

public sealed class UserDashboardPreference
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string? ActivityTypesJson { get; set; }

    public DateTime? LastViewedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public User User { get; set; } = null!;
}
