using System;

namespace TryOutSpot.Web.Data.Entities;

public partial class FlyerImport
{
    public Guid Id { get; set; }

    public string SourcePlatform { get; set; } = null!;

    public string? SourceUrl { get; set; }

    public string? OriginalExternalImageUrl { get; set; }

    public string? StoredObjectKey { get; set; }

    public string? StoredFileName { get; set; }

    public string? StoredContentType { get; set; }

    public string? ContentHash { get; set; }

    public string Status { get; set; } = null!;

    public Guid? SportId { get; set; }

    public string? SportName { get; set; }

    public string? OpportunityType { get; set; }

    public string? Title { get; set; }

    public string? TeamName { get; set; }

    public string? OrganizationName { get; set; }

    public string? AgeGroup { get; set; }

    public string? CompetitionLevel { get; set; }

    public DateTime? EventDate { get; set; }

    public DateTime? EventEndDate { get; set; }

    public DateTime? RegistrationDeadline { get; set; }

    public decimal? RegistrationFee { get; set; }

    public string? Location { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? ZipCode { get; set; }

    public string? ContactEmail { get; set; }

    public string? ContactPhone { get; set; }

    public string? WebsiteUrl { get; set; }

    public string? Description { get; set; }

    public string? RequiredEquipment { get; set; }

    public string? WhatToBring { get; set; }

    public string? SpecialInstructions { get; set; }

    public string? ExtractedJson { get; set; }

    public string? ConfidenceJson { get; set; }

    public string? AdminNotes { get; set; }

    public Guid CreatedByUserId { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public Guid? TeamId { get; set; }

    public Guid? OpportunityId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User CreatedByUser { get; set; } = null!;

    public virtual User? ReviewedByUser { get; set; }

    public virtual Sport? Sport { get; set; }

    public virtual Team? Team { get; set; }

    public virtual Opportunity? Opportunity { get; set; }
}
