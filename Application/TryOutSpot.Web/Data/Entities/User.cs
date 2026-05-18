using Microsoft.AspNetCore.Identity;

namespace TryOutSpot.Web.Data.Entities;

public partial class User : IdentityUser<Guid>
{
    private DateTime? dateOfBirth;

    public string FirstName { get; set; } = null!;

    public string LastName { get; set; } = null!;

    public DateTime? DateOfBirth
    {
        get => dateOfBirth;
        set
        {
            if (!value.HasValue)
            {
                dateOfBirth = null;
                return;
            }

            var normalized = value.Value;
            dateOfBirth = normalized.Kind switch
            {
                DateTimeKind.Utc => normalized,
                DateTimeKind.Local => normalized.ToUniversalTime(),
                _ => DateTime.SpecifyKind(normalized, DateTimeKind.Utc)
            };
        }
    }

    public string? ProfileImageUrl { get; set; }

    public string? ZipCode { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? NotificationPreferences { get; set; }

    public bool SmsConsentAccepted { get; set; }

    public DateTime? SmsConsentAcceptedAt { get; set; }

    public string? SmsConsentText { get; set; }

    public string? SmsConsentSource { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>();

    public virtual ICollection<Medium> Media { get; set; } = new List<Medium>();

    public virtual ICollection<Post> Posts { get; set; } = new List<Post>();

    public virtual ICollection<PlayerListing> PlayerListings { get; set; } = new List<PlayerListing>();

    public virtual ICollection<Registration> Registrations { get; set; } = new List<Registration>();

    public virtual ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();

    public virtual UserAdFrequency? UserAdFrequency { get; set; }

    public virtual UserDashboardPreference? UserDashboardPreference { get; set; }

    public virtual ICollection<UserFavorite> UserFavorites { get; set; } = new List<UserFavorite>();

    public virtual ICollection<UserOauthProvider> UserOauthProviders { get; set; } = new List<UserOauthProvider>();

    public virtual ICollection<UserPlayerRelationship> UserPlayerRelationships { get; set; } = new List<UserPlayerRelationship>();

    public virtual ICollection<UserTeamRole> UserTeamRoles { get; set; } = new List<UserTeamRole>();
}
