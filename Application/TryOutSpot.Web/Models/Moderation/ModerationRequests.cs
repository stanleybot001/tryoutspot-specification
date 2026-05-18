using System.ComponentModel.DataAnnotations;

namespace TryOutSpot.Web.Models.Moderation;

public sealed class ReviewListingReportRequest
{
    [Required]
    [MaxLength(30)]
    public string Status { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? AdminNotes { get; set; }
}
