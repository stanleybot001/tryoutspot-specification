namespace TryOutSpot.Web.Models.Listings;

public sealed record ListingReportActionResponse(
    string Message,
    ListingReportSummaryResponse Report);

public sealed record ListingReportSummaryResponse(
    Guid ReportId,
    string TargetType,
    Guid TargetId,
    string Reason,
    string? Details,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
