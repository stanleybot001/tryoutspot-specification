using System;
using System.Collections.Generic;

namespace TryOutSpot.Web.Data.Entities;

public partial class PlayerListing
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid? PlayerId { get; set; }

    public Guid? SportId { get; set; }

    public string ListingType { get; set; } = null!;

    public string Title { get; set; } = null!;

    public string? Description { get; set; }

    public decimal? AskingPrice { get; set; }

    public string? Currency { get; set; }

    public string? Condition { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? ZipCode { get; set; }

    public string? VisibleSocialLinkKeys { get; set; }

    public string? UploadedPdfObjectKey { get; set; }

    public string? UploadedPdfFileName { get; set; }

    public bool IsPublished { get; set; }

    public bool IsSearchable { get; set; }

    public DateTime? PublishedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public bool IsActive { get; set; }

    public virtual Player? Player { get; set; }

    public virtual Sport? Sport { get; set; }

    public virtual User User { get; set; } = null!;

    public virtual ICollection<ListingReport> ListingReports { get; set; } = new List<ListingReport>();

    public virtual ICollection<UserFavorite> UserFavorites { get; set; } = new List<UserFavorite>();
}
