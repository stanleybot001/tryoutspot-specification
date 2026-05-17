using System.ComponentModel.DataAnnotations;

namespace TryOutSpot.Web.Models.TeamListings;

/// <summary>
/// Request to create a team opportunity listing.
/// </summary>
public sealed class CreateTeamOpportunityRequest
{
    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    [Required]
    [MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Description { get; set; }

    [Required]
    public Guid SportId { get; set; }

    [MaxLength(100)]
    public string? CompetitionLevel { get; set; }

    [MaxLength(50)]
    public string? AgeGroup { get; set; }

    [Range(typeof(decimal), "0", "9999999")]
    public decimal RegistrationFee { get; set; }

    public bool RegistrationRequired { get; set; } = true;

    [Range(1, 10000)]
    public int? MaxParticipants { get; set; }

    public IReadOnlyCollection<string> RequiredRegistrationFieldCodes { get; set; } = [];

    public bool WaiverRequired { get; set; }

    [MaxLength(40)]
    public string? WaiverMethod { get; set; }

    public bool WaiverReturnByEmail { get; set; }

    public bool WaiverReturnInPerson { get; set; }

    public DateTime? RegistrationDeadline { get; set; }

    public DateTime? EventDate { get; set; }

    public DateTime? EventEndDate { get; set; }

    public DateTime? ListingStartDate { get; set; }

    public DateTime? ListingEndDate { get; set; }

    [MaxLength(500)]
    public string? Location { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(2)]
    public string? State { get; set; }

    [MaxLength(10)]
    public string? ZipCode { get; set; }

    [EmailAddress]
    [MaxLength(255)]
    public string? ContactEmail { get; set; }

    [Phone]
    [MaxLength(20)]
    public string? ContactPhone { get; set; }

    [MaxLength(500)]
    public string? WebsiteUrl { get; set; }

    [MaxLength(500)]
    public string? PdfUrl { get; set; }

    [MaxLength(2000)]
    public string? RequiredEquipment { get; set; }

    [MaxLength(2000)]
    public string? WhatToBring { get; set; }

    [MaxLength(2000)]
    public string? SpecialInstructions { get; set; }

    public bool IsPublished { get; set; } = true;

    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Request to update an existing team opportunity listing.
/// </summary>
public sealed class UpdateTeamOpportunityRequest
{
    [Required]
    [MaxLength(50)]
    public string Type { get; set; } = string.Empty;

    [Required]
    [MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Description { get; set; }

    [Required]
    public Guid SportId { get; set; }

    [MaxLength(100)]
    public string? CompetitionLevel { get; set; }

    [MaxLength(50)]
    public string? AgeGroup { get; set; }

    [Range(typeof(decimal), "0", "9999999")]
    public decimal RegistrationFee { get; set; }

    public bool RegistrationRequired { get; set; } = true;

    [Range(1, 10000)]
    public int? MaxParticipants { get; set; }

    public IReadOnlyCollection<string> RequiredRegistrationFieldCodes { get; set; } = [];

    public bool WaiverRequired { get; set; }

    [MaxLength(40)]
    public string? WaiverMethod { get; set; }

    public bool WaiverReturnByEmail { get; set; }

    public bool WaiverReturnInPerson { get; set; }

    public DateTime? RegistrationDeadline { get; set; }

    public DateTime? EventDate { get; set; }

    public DateTime? EventEndDate { get; set; }

    public DateTime? ListingStartDate { get; set; }

    public DateTime? ListingEndDate { get; set; }

    [MaxLength(500)]
    public string? Location { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(2)]
    public string? State { get; set; }

    [MaxLength(10)]
    public string? ZipCode { get; set; }

    [EmailAddress]
    [MaxLength(255)]
    public string? ContactEmail { get; set; }

    [Phone]
    [MaxLength(20)]
    public string? ContactPhone { get; set; }

    [MaxLength(500)]
    public string? WebsiteUrl { get; set; }

    [MaxLength(500)]
    public string? PdfUrl { get; set; }

    [MaxLength(2000)]
    public string? RequiredEquipment { get; set; }

    [MaxLength(2000)]
    public string? WhatToBring { get; set; }

    [MaxLength(2000)]
    public string? SpecialInstructions { get; set; }

    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Request to publish or unpublish a team opportunity listing.
/// </summary>
public sealed class SetTeamOpportunityPublicationRequest
{
    public bool IsPublished { get; set; }
}
