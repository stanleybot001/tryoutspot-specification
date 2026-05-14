using System;
using System.Collections.Generic;

namespace TryOutSpot.Web.Data.Entities;

public partial class Player
{
    public Guid Id { get; set; }

    public string FirstName { get; set; } = null!;

    public string LastName { get; set; } = null!;

    public DateTime DateOfBirth { get; set; }

    public string? Gender { get; set; }

    public string? ProfileImageUrl { get; set; }

    public string? Biography { get; set; }

    public string? Height { get; set; }

    public string? Weight { get; set; }

    public string? ThrowsHand { get; set; }

    public string? BatsHand { get; set; }

    public string? ContactEmail { get; set; }

    public string? ContactPhone { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? ZipCode { get; set; }

    public string? SchoolName { get; set; }

    public string? CurrentTeamName { get; set; }

    public int? GraduationYear { get; set; }

    public string ContactVisibility { get; set; } = "VerifiedCoachesOnly";

    public string? SocialMediaLinks { get; set; }

    public string? RecruitingProfileLinks { get; set; }

    public bool IsSearchable { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<PlayerSport> PlayerSports { get; set; } = new List<PlayerSport>();

    public virtual ICollection<Registration> Registrations { get; set; } = new List<Registration>();

    public virtual ICollection<UserPlayerRelationship> UserPlayerRelationships { get; set; } = new List<UserPlayerRelationship>();
}
