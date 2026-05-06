using Microsoft.AspNetCore.Identity;

namespace TryOutSpot.Web.Data.Entities;

public partial class User : IdentityUser<Guid>
{
    public string FirstName { get; set; } = null!;

    public string LastName { get; set; } = null!;

    public DateTime? DateOfBirth { get; set; }

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

    public virtual ICollection<Registration> Registrations { get; set; } = new List<Registration>();

    public virtual Subscription? Subscription { get; set; }

    public virtual UserAdFrequency? UserAdFrequency { get; set; }

    public virtual ICollection<UserOauthProvider> UserOauthProviders { get; set; } = new List<UserOauthProvider>();

    public virtual ICollection<UserPlayerRelationship> UserPlayerRelationships { get; set; } = new List<UserPlayerRelationship>();

    public virtual ICollection<UserTeamRole> UserTeamRoles { get; set; } = new List<UserTeamRole>();
}
