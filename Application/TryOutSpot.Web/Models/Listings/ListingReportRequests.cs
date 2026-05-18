using System.ComponentModel.DataAnnotations;

namespace TryOutSpot.Web.Models.Listings;

public sealed class ReportListingRequest
{
    [Required]
    [MaxLength(100)]
    public string Reason { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Details { get; set; }
}
