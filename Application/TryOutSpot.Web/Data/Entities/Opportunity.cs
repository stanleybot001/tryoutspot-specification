using System;
using System.Collections.Generic;

namespace TryOutSpot.Web.Data.Entities;

public partial class Opportunity
{
    public Guid Id { get; set; }

    public Guid TeamId { get; set; }

    public Guid SportId { get; set; }

    public string Type { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public string? CompetitionLevel { get; set; }

    public string? AgeGroup { get; set; }

    public string? GenderRequirement { get; set; }

    public int? MaxParticipants { get; set; }

    public bool RegistrationRequired { get; set; }

    public DateTime? RegistrationDeadline { get; set; }

    public decimal RegistrationFee { get; set; }

    public DateTime? EventDate { get; set; }

    public DateTime? EventEndDate { get; set; }

    public string? Location { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? ZipCode { get; set; }

    public string? ContactEmail { get; set; }

    public string? ContactPhone { get; set; }

    public string? WebsiteUrl { get; set; }

    public string? RequiredEquipment { get; set; }

    public string? WhatToBring { get; set; }

    public string? SpecialInstructions { get; set; }

    public bool IsPublished { get; set; }

    public DateTime? PublishedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public int ViewCount { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool IsActive { get; set; }

    public virtual ICollection<OpportunityGeographicTarget> OpportunityGeographicTargets { get; set; } = new List<OpportunityGeographicTarget>();

    public virtual ICollection<Post> Posts { get; set; } = new List<Post>();

    public virtual ICollection<Registration> Registrations { get; set; } = new List<Registration>();

    public virtual Sport Sport { get; set; } = null!;

    public virtual Team Team { get; set; } = null!;
}
