using System;
using System.Collections.Generic;

namespace TryOutSpot.Web.Data.Entities;

public partial class Team
{
    public Guid Id { get; set; }

    public Guid? OrganizationId { get; set; }

    public string Name { get; set; } = null!;

    public string? TeamLevel { get; set; }

    public string GeographicScope { get; set; } = "Local";

    public string? Description { get; set; }

    public string? LogoImageUrl { get; set; }

    public string? PrimaryColor { get; set; }

    public string? SecondaryColor { get; set; }

    public int? EstablishedYear { get; set; }

    public string? WebsiteUrl { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? ZipCode { get; set; }

    public string? PhoneNumber { get; set; }

    public string? Email { get; set; }

    public string? SocialMediaLinks { get; set; }

    public bool IsSearchable { get; set; }

    public bool IsContactInfoVisible { get; set; }

    public bool IsElite { get; set; }

    public bool IsVerified { get; set; }

    public DateTime? VerificationDate { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<Opportunity> Opportunities { get; set; } = new List<Opportunity>();

    public virtual Organization? Organization { get; set; }

    public virtual ICollection<TeamSport> TeamSports { get; set; } = new List<TeamSport>();

    public virtual ICollection<UserTeamRole> UserTeamRoles { get; set; } = new List<UserTeamRole>();
}
