using System;

namespace TryOutSpot.Web.Data.Entities;

public partial class ListingReport
{
    public Guid Id { get; set; }

    public Guid ReporterUserId { get; set; }

    public Guid? PlayerListingId { get; set; }

    public Guid? OpportunityId { get; set; }

    public string Reason { get; set; } = null!;

    public string? Details { get; set; }

    public string Status { get; set; } = null!;

    public string? AdminNotes { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual Opportunity? Opportunity { get; set; }

    public virtual PlayerListing? PlayerListing { get; set; }

    public virtual User ReporterUser { get; set; } = null!;

    public virtual User? ReviewedByUser { get; set; }
}
