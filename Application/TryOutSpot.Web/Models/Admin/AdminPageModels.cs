using System.ComponentModel.DataAnnotations;

namespace TryOutSpot.Web.Models.Admin;

public sealed record AdminDashboardPageModel(
    int PendingReportCount,
    int InReviewReportCount,
    int PendingFlyerImportCount,
    int ActiveUserCount,
    int ActiveTeamCount,
    int ActivePlayerListingCount,
    int ActiveTeamOpportunityCount,
    IReadOnlyCollection<AdminReportListItem> RecentReports);

public sealed class AdminPromotionsPageModel
{
    public string PromotionCode { get; set; } = string.Empty;

    public string PromotionName { get; set; } = string.Empty;

    public bool IsEnabled { get; set; }

    public int ClaimedCount { get; set; }

    public int RemainingCount { get; set; }

    public int MaxClaims { get; set; }

    public int GrantMonths { get; set; }

    public int ActiveGrantCount { get; set; }

    public DateTime? LatestGrantEndsAtUtc { get; set; }

    public IReadOnlyCollection<AdminPromotionClaimItem> RecentClaims { get; set; } = [];

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public int TotalPages { get; set; }
}

public sealed class AdminPromotionSettingsForm
{
    [MaxLength(200)]
    public string? Name { get; set; }

    public bool IsEnabled { get; set; } = true;

    [Range(1, 100000)]
    public int MaxRedemptions { get; set; }

    [Range(1, 120)]
    public int GrantMonths { get; set; }
}

public sealed record AdminPromotionClaimItem(
    Guid UserId,
    string UserDisplayName,
    string UserEmail,
    IReadOnlyCollection<string> GrantedPlanCodes,
    int ActiveGrantCount,
    DateTime? LatestGrantEndsAtUtc,
    DateTime RedeemedAtUtc);

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

    public IReadOnlyCollection<AdminBillingPlanOption> EligibleComplimentaryGrantPlans { get; set; } = [];

    public IReadOnlyCollection<AdminComplimentaryGrantItem> ComplimentaryGrants { get; set; } = [];
}

public sealed record AdminUserSummaryItem(
    Guid UserId,
    string DisplayName,
    string Email,
    bool IsActive);

public sealed record AdminBillingPlanOption(
    string Code,
    string Name,
    string Audience);

public sealed record AdminComplimentaryGrantItem(
    Guid GrantId,
    string PlanCode,
    string PlanName,
    string Status,
    bool HasActiveEntitlement,
    string ScopeType,
    Guid? ScopeId,
    DateTime StartsAtUtc,
    DateTime? EndsAtUtc,
    string Source,
    string? PromotionCode,
    string? Reason,
    Guid GrantedByUserId,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    DateTime? RevokedAtUtc,
    Guid? RevokedByUserId,
    string? RevokeReason);

public sealed class AdminCreateComplimentaryGrantForm
{
    [Required]
    [MaxLength(50)]
    public string PlanCode { get; set; } = string.Empty;

    [Range(1, 120)]
    public int? DurationMonths { get; set; }

    public DateTime? StartsAt { get; set; }

    public DateTime? EndsAt { get; set; }

    [MaxLength(1000)]
    public string? Reason { get; set; }

    public string? ReturnUrl { get; set; }
}

public sealed class AdminRevokeComplimentaryGrantForm
{
    [MaxLength(1000)]
    public string? Reason { get; set; }

    public string? ReturnUrl { get; set; }
}

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

public sealed class AdminTeamEditPageModel
{
    public Guid TeamId { get; set; }

    public string? ReturnUrl { get; set; }

    public AdminTeamEditForm Form { get; set; } = new();
}

public sealed class AdminTeamEditForm
{
    [Required]
    [MaxLength(200)]
    [Display(Name = "Team name")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(50)]
    [Display(Name = "Team level")]
    public string? TeamLevel { get; set; }

    [MaxLength(50)]
    [Display(Name = "Geographic scope")]
    public string GeographicScope { get; set; } = "Local";

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(500)]
    [Display(Name = "Website URL")]
    public string? WebsiteUrl { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(2)]
    public string? State { get; set; }

    [MaxLength(10)]
    [Display(Name = "ZIP code")]
    public string? ZipCode { get; set; }

    [Phone]
    [MaxLength(20)]
    [Display(Name = "Phone")]
    public string? PhoneNumber { get; set; }

    [EmailAddress]
    [MaxLength(255)]
    public string? Email { get; set; }

    [Display(Name = "Searchable")]
    public bool IsSearchable { get; set; }

    [Display(Name = "Show contact info")]
    public bool IsContactInfoVisible { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; }
}

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
    bool TeamIsActive,
    bool IsActive,
    bool IsFlyerCreated,
    DateTime? EventDateUtc,
    DateTime? ListingStartDateUtc,
    DateTime? ListingEndDateUtc,
    DateTime? ExpiresAtUtc,
    int OpenReportCount,
    int TotalReportCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed class AdminTeamOpportunityEditPageModel
{
    public Guid OpportunityId { get; set; }

    public Guid TeamId { get; set; }

    public string? ReturnUrl { get; set; }

    public AdminTeamOpportunityFlyerReference? Flyer { get; set; }

    public IReadOnlyCollection<AdminSportOption> SportOptions { get; set; } = [];

    public AdminTeamOpportunityEditForm Form { get; set; } = new();
}

public sealed record AdminTeamOpportunityFlyerReference(
    string? FileName,
    string? ContentType,
    string? StoredFlyerUrl,
    string? SourceUrl,
    string? ExternalImageUrl);

public sealed class AdminTeamOpportunityEditForm
{
    [Required]
    [MaxLength(200)]
    [Display(Name = "Team name")]
    public string TeamName { get; set; } = string.Empty;

    [MaxLength(50)]
    [Display(Name = "Team level")]
    public string? TeamLevel { get; set; }

    [MaxLength(500)]
    [Display(Name = "Team address")]
    public string? TeamAddress { get; set; }

    [MaxLength(100)]
    [Display(Name = "Team city")]
    public string? TeamCity { get; set; }

    [MaxLength(2)]
    [Display(Name = "Team state")]
    public string? TeamState { get; set; }

    [MaxLength(10)]
    [Display(Name = "Team ZIP")]
    public string? TeamZipCode { get; set; }

    [Required]
    [MaxLength(300)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    [Display(Name = "Type")]
    public string Type { get; set; } = "tryout";

    [Required]
    [Display(Name = "Sport")]
    public Guid SportId { get; set; }

    [MaxLength(50)]
    [Display(Name = "Age group")]
    public string? AgeGroup { get; set; }

    [MaxLength(100)]
    [Display(Name = "Competition level")]
    public string? CompetitionLevel { get; set; }

    [MaxLength(4000)]
    public string? Description { get; set; }

    [DataType(DataType.DateTime)]
    [Display(Name = "Event starts")]
    public DateTime? EventDate { get; set; }

    [DataType(DataType.DateTime)]
    [Display(Name = "Event ends")]
    public DateTime? EventEndDate { get; set; }

    [DataType(DataType.DateTime)]
    [Display(Name = "Listing starts")]
    public DateTime? ListingStartDate { get; set; }

    [DataType(DataType.DateTime)]
    [Display(Name = "Listing ends")]
    public DateTime? ListingEndDate { get; set; }

    [MaxLength(500)]
    [Display(Name = "Location name")]
    public string? Location { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(2)]
    public string? State { get; set; }

    [MaxLength(10)]
    [Display(Name = "ZIP code")]
    public string? ZipCode { get; set; }

    [EmailAddress]
    [MaxLength(255)]
    [Display(Name = "Contact email")]
    public string? ContactEmail { get; set; }

    [Phone]
    [MaxLength(20)]
    [Display(Name = "Contact phone")]
    public string? ContactPhone { get; set; }

    [MaxLength(500)]
    [Display(Name = "Website URL")]
    public string? WebsiteUrl { get; set; }

    [MaxLength(1000)]
    [Display(Name = "Required equipment")]
    public string? RequiredEquipment { get; set; }

    [MaxLength(1000)]
    [Display(Name = "What to bring")]
    public string? WhatToBring { get; set; }

    [MaxLength(2000)]
    [Display(Name = "Special instructions")]
    public string? SpecialInstructions { get; set; }

    [Display(Name = "Published")]
    public bool IsPublished { get; set; }

    [Display(Name = "Active")]
    public bool IsActive { get; set; }
}

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
