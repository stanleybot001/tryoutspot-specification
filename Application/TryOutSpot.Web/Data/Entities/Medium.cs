using System;
using System.Collections.Generic;

namespace TryOutSpot.Web.Data.Entities;

public partial class Medium
{
    public Guid Id { get; set; }

    public string EntityType { get; set; } = null!;

    public Guid EntityId { get; set; }

    public string MediaType { get; set; } = null!;

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string Url { get; set; } = null!;

    public string? ThumbnailUrl { get; set; }

    public long? FileSize { get; set; }

    public string? MimeType { get; set; }

    public int? Duration { get; set; }

    public int SortOrder { get; set; }

    public bool IsPublic { get; set; }

    public bool IsFeatured { get; set; }

    public Guid UploadedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    public bool IsActive { get; set; }

    public virtual User UploadedByUser { get; set; } = null!;
}
