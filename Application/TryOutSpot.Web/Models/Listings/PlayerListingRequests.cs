using System.ComponentModel.DataAnnotations;

namespace TryOutSpot.Web.Models.Listings;

/// <summary>
/// Request to create a parent or player listing.
/// </summary>
public sealed class CreatePlayerListingRequest
{
    [Required]
    [MaxLength(50)]
    public string ListingType { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Description { get; set; }

    public Guid? PlayerId { get; set; }

    public Guid? SportId { get; set; }

    [Range(typeof(decimal), "0", "9999999")]
    public decimal? AskingPrice { get; set; }

    [MaxLength(3)]
    public string? Currency { get; set; }

    [MaxLength(50)]
    public string? Condition { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(2)]
    public string? State { get; set; }

    [MaxLength(10)]
    public string? ZipCode { get; set; }

    public bool IsSearchable { get; set; } = true;

    public bool IsPublished { get; set; } = true;

    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Request to update a parent or player listing.
/// </summary>
public sealed class UpdatePlayerListingRequest
{
    [Required]
    [MaxLength(50)]
    public string ListingType { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4000)]
    public string? Description { get; set; }

    public Guid? PlayerId { get; set; }

    public Guid? SportId { get; set; }

    [Range(typeof(decimal), "0", "9999999")]
    public decimal? AskingPrice { get; set; }

    [MaxLength(3)]
    public string? Currency { get; set; }

    [MaxLength(50)]
    public string? Condition { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(2)]
    public string? State { get; set; }

    [MaxLength(10)]
    public string? ZipCode { get; set; }

    public bool IsSearchable { get; set; } = true;

    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Request to publish or unpublish a listing.
/// </summary>
public sealed class SetPlayerListingPublicationRequest
{
    public bool IsPublished { get; set; }
}
