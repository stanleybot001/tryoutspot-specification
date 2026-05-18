using System.ComponentModel.DataAnnotations;

namespace TryOutSpot.Web.Models.Admin;

public sealed record AdminDashboardPageModel(
    int PendingReportCount,
    int InReviewReportCount,
    int ActiveUserCount,
    int ActiveTeamCount,
    int ActivePlayerListingCount,
    int ActiveTeamOpportunityCount,
    IReadOnlyCollection<AdminReportListItem> RecentReports);

public sealed class AdminReportListPageModel
{
    public IReadOnlyCollection<AdminReportListItem> Reports { get; set; } = [];

    public string? Status { get; set; }

    public string? TargetType { get; set; }

    public string? Search { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public int TotalPages { get; set; }

    public IReadOnlyCollection<string> StatusOptions { get; set; } = [];

    public IReadOnlyCollection<string> TargetTypeOptions { get; set; } = [];
}

public sealed record AdminReportListItem(
    Guid ReportId,
    string TargetType,
    Guid TargetId,
    string TargetTitle,
    string ReporterName,
    string ReporterEmail,
    Guid ReporterUserId,
    string Reason,
    string? Details,
    string Status,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    string? ReviewedByName,
    DateTime? ReviewedAtUtc);

public sealed class AdminReportDetailPageModel
{
    public AdminReportListItem Report { get; set; } = null!;

    public string? AdminNotes { get; set; }

    public AdminUserSummaryItem Reporter { get; set; } = null!;

    public AdminUserSummaryItem? ReviewedBy { get; set; }

    public AdminPlayerListingDetailItem? PlayerListing { get; set; }

    public AdminTeamOpportunityDetailItem? TeamOpportunity { get; set; }

    public IReadOnlyCollection<string> StatusOptions { get; set; } = [];
}

public sealed class AdminReviewReportForm
{
    [Required]
    [MaxLength(30)]
    public string Status { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? AdminNotes { get; set; }
}

public sealed class AdminUserListPageModel
{
    public IReadOnlyCollection<AdminUserListItem> Users { get; set; } = [];

    public Guid CurrentAdminUserId { get; set; }

    public string? Search { get; set; }

    public string? AccountType { get; set; }

    public bool? IsActive { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public int TotalPages { get; set; }

    public IReadOnlyCollection<string> AccountTypeOptions { get; set; } = [];
}

public sealed record AdminUserListItem(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    IReadOnlyCollection<string> AccountTypes,
    bool IsActive,
    bool IsLockedOut,
    bool EmailConfirmed,
    bool PhoneNumberConfirmed,
    int PlayerProfileCount,
    int PlayerListingCount,
    int TeamCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed class AdminUserDetailPageModel
{
    public AdminUserListItem User { get; set; } = null!;

    public Guid CurrentAdminUserId { get; set; }

    public string? PhoneNumber { get; set; }

    public DateTime? DateOfBirth { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? ZipCode { get; set; }

    public IReadOnlyCollection<AdminPlayerProfileItem> PlayerProfiles { get; set; } = [];

    public IReadOnlyCollection<AdminPlayerListingListItem> PlayerListings { get; set; } = [];

    public IReadOnlyCollection<AdminTeamProfileItem> Teams { get; set; } = [];

    public IReadOnlyCollection<AdminTeamOpportunityListItem> TeamOpportunities { get; set; } = [];
}

public sealed record AdminUserSummaryItem(
    Guid UserId,
    string DisplayName,
    string Email,
    bool IsActive);

public sealed record AdminPlayerProfileItem(
    Guid PlayerId,
    string DisplayName,
    DateTime DateOfBirth,
    string? City,
    string? State,
    string? ZipCode,
    string Relationship,
    bool CanManage,
    bool IsSearchable,
    bool IsActive,
    IReadOnlyCollection<string> Sports);

public sealed class AdminListingListPageModel<TListing>
{
    public IReadOnlyCollection<TListing> Listings { get; set; } = [];

    public string? Search { get; set; }

    public bool? IsPublished { get; set; }

    public bool? IsActive { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public int TotalPages { get; set; }
}

public sealed class AdminTeamListPageModel
{
    public IReadOnlyCollection<AdminTeamListItem> Teams { get; set; } = [];

    public string? Search { get; set; }

    public bool? IsActive { get; set; }

    public bool? IsSearchable { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public int TotalPages { get; set; }
}

public sealed record AdminTeamListItem(
    Guid TeamId,
    string TeamName,
    string? OrganizationName,
    string? TeamLevel,
    string GeographicScope,
    string? City,
    string? State,
    string? ZipCode,
    bool IsSearchable,
    bool IsContactInfoVisible,
    bool IsActive,
    int RepresentativeCount,
    int OpportunityCount,
    int ActiveOpportunityCount,
    int OpenReportCount,
    IReadOnlyCollection<string> Sports,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record AdminPlayerListingListItem(
    Guid ListingId,
    Guid OwnerUserId,
    string OwnerName,
    string OwnerEmail,
    string ListingType,
    string Title,
    string? Description,
    string? PlayerName,
    string? SportName,
    string? City,
    string? State,
    string? ZipCode,
    bool IsPublished,
    bool IsSearchable,
    bool IsActive,
    int OpenReportCount,
    int TotalReportCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record AdminPlayerListingDetailItem(
    Guid ListingId,
    Guid OwnerUserId,
    string OwnerName,
    string OwnerEmail,
    string ListingType,
    string Title,
    string? Description,
    string? PlayerName,
    string? SportName,
    string? City,
    string? State,
    string? ZipCode,
    bool IsPublished,
    bool IsSearchable,
    bool IsActive,
    int OpenReportCount,
    int TotalReportCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record AdminTeamProfileItem(
    Guid TeamId,
    string TeamName,
    string? OrganizationName,
    string Role,
    string? TeamLevel,
    string GeographicScope,
    string? City,
    string? State,
    string? ZipCode,
    bool IsSearchable,
    bool IsContactInfoVisible,
    bool IsActive,
    IReadOnlyCollection<string> Sports);

public sealed record AdminTeamOpportunityListItem(
    Guid OpportunityId,
    Guid TeamId,
    string TeamName,
    string? OrganizationName,
    string Type,
    string Title,
    string? Description,
    string SportName,
    string? City,
    string? State,
    string? ZipCode,
    bool IsPublished,
    bool TeamIsSearchable,
    bool IsActive,
    int OpenReportCount,
    int TotalReportCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record AdminTeamOpportunityDetailItem(
    Guid OpportunityId,
    Guid TeamId,
    string TeamName,
    string? OrganizationName,
    string Type,
    string Title,
    string? Description,
    string SportName,
    string? City,
    string? State,
    string? ZipCode,
    bool IsPublished,
    bool TeamIsSearchable,
    bool IsActive,
    int OpenReportCount,
    int TotalReportCount,
    IReadOnlyCollection<AdminUserSummaryItem> TeamRepresentatives,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
