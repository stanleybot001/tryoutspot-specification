using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using TryOutSpot.Web.Billing;
using TryOutSpot.Web.Models.Billing;
using TryOutSpot.Web.Models.Dashboard;

namespace TryOutSpot.Web.Models.WebAccount;

public sealed class RegisterPageModel
{
    [Required]
    [EmailAddress]
    [MaxLength(255)]
    [Display(Name = "Email address")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [Compare(nameof(Password))]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Phone]
    [MaxLength(20)]
    [Display(Name = "Phone number")]
    public string? PhoneNumber { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Date of birth")]
    public DateTime? DateOfBirth { get; set; }

    [MaxLength(10)]
    [Display(Name = "ZIP code")]
    public string? ZipCode { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(2)]
    public string? State { get; set; }

    [Display(Name = "SMS consent")]
    public bool SmsConsentAccepted { get; set; }

    public List<string> AccountTypes { get; set; } = [];

    public IReadOnlyCollection<AccountTypeSelectionItem> AvailableAccountTypes { get; set; } = [];

    public string? ReturnUrl { get; set; }

    public bool GoogleIsConfigured { get; set; }

    public bool FacebookIsConfigured { get; set; }

    public bool AppleIsConfigured { get; set; }
}

public sealed class LoginPageModel
{
    [Required]
    [EmailAddress]
    [MaxLength(255)]
    [Display(Name = "Email address")]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Keep me signed in")]
    public bool RememberMe { get; set; } = true;

    public string? ReturnUrl { get; set; }

    public bool ShowResendVerificationPrompt { get; set; }

    public bool GoogleIsConfigured { get; set; }

    public bool FacebookIsConfigured { get; set; }

    public bool AppleIsConfigured { get; set; }
}

public sealed class ExternalLoginBrowserWarningPageModel
{
    public string Provider { get; set; } = string.Empty;

    public string ProviderDisplayName { get; set; } = string.Empty;

    public string BrowserDisplayName { get; set; } = "this app's browser";

    public string ContinueInBrowserUrl { get; set; } = string.Empty;

    public string EmailFallbackUrl { get; set; } = "/account/login";

    public string? AndroidChromeIntentUrl { get; set; }
}

public sealed class ForgotPasswordPageModel
{
    [Required]
    [EmailAddress]
    [MaxLength(255)]
    [Display(Name = "Email address")]
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordPageModel
{
    [Required]
    [EmailAddress]
    [MaxLength(255)]
    [Display(Name = "Email address")]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Token { get; set; } = string.Empty;

    [Required]
    [MinLength(8)]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    [Compare(nameof(NewPassword))]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    public string ConfirmNewPassword { get; set; } = string.Empty;
}

public sealed class SocialRegistrationPageModel
{
    [Required]
    public string ExternalLoginToken { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public bool EmailVerified { get; set; }

    public string? ProfileImageUrl { get; set; }

    [Required]
    [MaxLength(100)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [Phone]
    [MaxLength(20)]
    [Display(Name = "Phone number")]
    public string? PhoneNumber { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Date of birth")]
    public DateTime? DateOfBirth { get; set; }

    [MaxLength(10)]
    [Display(Name = "ZIP code")]
    public string? ZipCode { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(2)]
    public string? State { get; set; }

    [Display(Name = "SMS consent")]
    public bool SmsConsentAccepted { get; set; }

    public List<string> AccountTypes { get; set; } = [];

    public IReadOnlyCollection<AccountTypeSelectionItem> AvailableAccountTypes { get; set; } = [];

    public bool ExistingEmailAccountFound { get; set; }

    public bool CanLinkToSignedInAccount { get; set; }

    public string? ReturnUrl { get; set; }
}

public sealed class OnboardingPageModel
{
    public string Email { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public bool EmailConfirmed { get; set; }

    public bool PhoneNumberConfirmed { get; set; }

    public string? PhoneNumber { get; set; }

    public bool SmsConsentAccepted { get; set; }

    public List<string> AccountTypes { get; set; } = [];

    public IReadOnlyCollection<AccountTypeSelectionItem> AvailableAccountTypes { get; set; } = [];

    public IReadOnlyCollection<BillingPlanResponse> RecommendedPlans { get; set; } = [];

    public bool ShowRecommendedPlans { get; set; } = true;

    public LaunchPromotionPageItem? LaunchPromotion { get; set; }

    public IReadOnlyCollection<OnboardingStepPageItem> Steps { get; set; } = [];

    public IReadOnlyCollection<string> FeatureCodes { get; set; } = [];

    public bool ShowTryoutRegistrationList { get; set; }

    public IReadOnlyCollection<OnboardingTryoutRegistrationPageItem> UpcomingTryoutRegistrations { get; set; } = [];

    public IReadOnlyCollection<DashboardPlayerListingFavoritePageItem> FavoritePlayerListings { get; set; } = [];

    public IReadOnlyCollection<DashboardOpportunityFavoritePageItem> FavoriteOpportunities { get; set; } = [];

    public DashboardRecentActivityResponse? RecentActivity { get; set; }

    public ActivationAssistancePromptResponse? ActivationAssistance { get; set; }

    public IReadOnlyCollection<FlyerTeamClaimPageItem> ClaimableFlyerTeams { get; set; } = [];
}

public sealed class FavoritesPageModel
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public IReadOnlyCollection<FavoriteListPageItem> Favorites { get; set; } = [];

    public int FavoriteCount { get; set; }

    public int OpportunityFavoriteCount { get; set; }

    public int PlayerListingFavoriteCount { get; set; }

    public int CurrentPage { get; set; } = 1;

    public int PageSize { get; set; } = 25;

    public int TotalPages { get; set; } = 1;

    public IReadOnlyCollection<int> PageSizeOptions { get; set; } = [];

    public bool HasPreviousPage => CurrentPage > 1;

    public bool HasNextPage => CurrentPage < TotalPages;

    public int FirstItemNumber => FavoriteCount == 0 ? 0 : ((CurrentPage - 1) * PageSize) + 1;

    public int LastItemNumber => Math.Min(CurrentPage * PageSize, FavoriteCount);
}

public sealed record FavoriteListPageItem(
    Guid TargetId,
    bool IsOpportunity,
    string CategoryLabel,
    string TypeLabel,
    string Title,
    string? PrimaryName,
    string? SecondaryName,
    string? SportName,
    DateTime? EventDate,
    string? City,
    string? State,
    string? ZipCode,
    bool IsAvailable,
    DateTime CreatedAt);

public sealed class AddPlayerProfilePageModel
{
    public Guid? PlayerId { get; set; }

    public bool IsEditMode { get; set; }

    public string? ReturnUrl { get; set; }

    [Required]
    [MaxLength(100)]
    [Display(Name = "Player first name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [Display(Name = "Player last name")]
    public string LastName { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Date)]
    [Display(Name = "Player date of birth")]
    public DateTime DateOfBirth { get; set; }

    [Required]
    [MaxLength(50)]
    [Display(Name = "Relationship")]
    public string Relationship { get; set; } = "Parent";

    [Display(Name = "Can manage this player profile")]
    public bool CanManage { get; set; } = true;

    [Display(Name = "Allow this profile on public links and listings")]
    public bool IsSearchable { get; set; }

    [Required]
    [MaxLength(40)]
    [Display(Name = "Contact visibility")]
    public string ContactVisibility { get; set; } = "VerifiedCoachesOnly";

    [EmailAddress]
    [MaxLength(255)]
    [Display(Name = "Contact email")]
    public string? ContactEmail { get; set; }

    [Phone]
    [MaxLength(20)]
    [Display(Name = "Contact phone")]
    public string? ContactPhone { get; set; }

    [MaxLength(500)]
    [Display(Name = "Profile picture URL")]
    public string? ProfileImageUrl { get; set; }

    [Display(Name = "Upload profile picture")]
    public IFormFile? ProfileImageUpload { get; set; }

    [Display(Name = "Remove uploaded profile picture")]
    public bool RemoveProfileImage { get; set; }

    public string? CurrentProfileImageUrl { get; set; }

    public bool EnhancedProfileVisibleToTeams { get; set; }

    public bool IsParentOrGuardianAccount { get; set; }

    public bool IsSelfPlayerAccount { get; set; }

    [MaxLength(500)]
    [Display(Name = "Highlight video link 1")]
    public string? HighlightVideoUrl1 { get; set; }

    [MaxLength(500)]
    [Display(Name = "Highlight video link 2")]
    public string? HighlightVideoUrl2 { get; set; }

    [MaxLength(200)]
    [Display(Name = "School name")]
    public string? SchoolName { get; set; }

    [MaxLength(200)]
    [Display(Name = "Club or team name")]
    public string? CurrentTeamName { get; set; }

    [Display(Name = "Graduation year")]
    [Range(1900, 2200)]
    public int? GraduationYear { get; set; }

    [MaxLength(20)]
    [Display(Name = "Height")]
    public string? Height { get; set; }

    [MaxLength(20)]
    [Display(Name = "Weight")]
    public string? Weight { get; set; }

    [MaxLength(10)]
    [Display(Name = "Throws hand")]
    public string? ThrowsHand { get; set; }

    [MaxLength(10)]
    [Display(Name = "Bats hand")]
    public string? BatsHand { get; set; }

    [MaxLength(20)]
    [Display(Name = "60-yard dash")]
    public string? SixtyYardDash { get; set; }

    [MaxLength(20)]
    [Display(Name = "Home-to-first time")]
    public string? HomeToFirstTime { get; set; }

    [MaxLength(20)]
    [Display(Name = "Exit velocity")]
    public string? ExitVelocity { get; set; }

    [MaxLength(20)]
    [Display(Name = "Throwing velocity")]
    public string? ThrowingVelocity { get; set; }

    [MaxLength(20)]
    [Display(Name = "Pitch velocity")]
    public string? PitchVelocity { get; set; }

    [MaxLength(20)]
    [Display(Name = "Catcher pop time")]
    public string? CatcherPopTime { get; set; }

    [MaxLength(1000)]
    [Display(Name = "Additional metrics")]
    public string? AdditionalMetrics { get; set; }

    [MaxLength(100)]
    [Display(Name = "City")]
    public string? City { get; set; }

    [MaxLength(2)]
    [Display(Name = "State")]
    public string? State { get; set; }

    [MaxLength(10)]
    [Display(Name = "ZIP code")]
    public string? ZipCode { get; set; }

    [MaxLength(500)]
    [Display(Name = "Facebook page")]
    public string? FacebookPageUrl { get; set; }

    [MaxLength(500)]
    [Display(Name = "X username")]
    public string? XPageUrl { get; set; }

    [MaxLength(500)]
    [Display(Name = "Instagram username")]
    public string? InstagramUrl { get; set; }

    [MaxLength(500)]
    [Display(Name = "YouTube")]
    public string? YouTubeUrl { get; set; }

    [MaxLength(500)]
    [Display(Name = "TikTok username")]
    public string? TikTokUrl { get; set; }

    [MaxLength(500)]
    [Display(Name = "SportsRecruits profile")]
    public string? SportsRecruitsProfileUrl { get; set; }

    [MaxLength(500)]
    [Display(Name = "FieldLevel profile")]
    public string? FieldLevelProfileUrl { get; set; }

    [MaxLength(500)]
    [Display(Name = "NCSA profile")]
    public string? NcsaProfileUrl { get; set; }

    [MaxLength(500)]
    [Display(Name = "Other recruiting profile")]
    public string? OtherRecruitingProfileUrl { get; set; }

    public List<PlayerSportDetailPageModel> SportDetails { get; set; } = [];

    public IReadOnlyCollection<SportSelectionPageItem> AvailableSports { get; set; } = [];

    public IReadOnlyCollection<string> AvailableContactVisibilityOptions { get; set; } = [];

    public IReadOnlyCollection<string> AvailableRelationshipOptions { get; set; } = [];
}

public sealed class ManagePlayerProfilesPageModel
{
    public IReadOnlyCollection<PlayerProfileSummaryPageModel> Profiles { get; set; } = [];

    public bool IsParentOrGuardianAccount { get; set; }
}

public sealed class PlayerProfileSummaryPageModel
{
    public Guid PlayerId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public DateTime DateOfBirth { get; set; }

    public string Relationship { get; set; } = string.Empty;

    public bool CanManage { get; set; }

    public bool IsSearchable { get; set; }

    public string ContactVisibility { get; set; } = string.Empty;

    public string? ContactEmail { get; set; }

    public string? ContactPhone { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? ZipCode { get; set; }

    public string[] Sports { get; set; } = [];

    public int ActiveListingCount { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public sealed class ChoosePlanPageModel
{
    [Required]
    [Display(Name = "Plan")]
    public string PlanCode { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Billing interval")]
    public string BillingInterval { get; set; } = "month";

    [MaxLength(50)]
    public string? BundleType { get; set; }

    public string BundleName { get; set; } = "Membership";

    public bool StripeIsConfigured { get; set; }

    public bool CheckoutAvailableForSelection { get; set; }

    public IReadOnlyCollection<string> AvailableBillingIntervals { get; set; } = [BillingIntervalCodes.Month];

    public bool SelectedPlanRequiresAnnualBilling { get; set; }

    public IReadOnlyCollection<BillingPlanResponse> AvailablePlans { get; set; } = [];
}

public sealed class AddTeamOrOrganizationPageModel
{
    [Display(Name = "Create")]
    public string CreateType { get; set; } = "team";

    public Guid? TeamId { get; set; }

    public bool IsEditMode { get; set; }

    [Required]
    [MaxLength(200)]
    [Display(Name = "Team name")]
    public string TeamName { get; set; } = string.Empty;

    [MaxLength(200)]
    [Display(Name = "Organization name")]
    public string? OrganizationName { get; set; }

    [MaxLength(50)]
    [Display(Name = "Account type")]
    public string TeamRole { get; set; } = string.Empty;

    [MaxLength(50)]
    [Display(Name = "Team level")]
    public string? TeamLevel { get; set; }

    [MaxLength(4000)]
    [Display(Name = "Team description")]
    public string? TeamDescription { get; set; }

    [Required]
    [MaxLength(20)]
    [Display(Name = "Coverage")]
    public string GeographicScope { get; set; } = "Local";

    [Display(Name = "Allow this team or organization to appear in search")]
    public bool IsSearchable { get; set; } = true;

    [EmailAddress]
    [MaxLength(255)]
    [Display(Name = "Contact email")]
    public string? ContactEmail { get; set; }

    [Phone]
    [MaxLength(20)]
    [Display(Name = "Contact phone")]
    public string? ContactPhone { get; set; }

    [MaxLength(500)]
    [Display(Name = "Profile picture URL")]
    public string? ProfileImageUrl { get; set; }

    [Display(Name = "Upload team logo")]
    public IFormFile? ProfileImageUpload { get; set; }

    [Display(Name = "Remove uploaded team logo")]
    public bool RemoveProfileImage { get; set; }

    public string? CurrentProfileImageUrl { get; set; }

    [MaxLength(500)]
    [Display(Name = "Highlight video link 1")]
    public string? HighlightVideoUrl1 { get; set; }

    [MaxLength(500)]
    [Display(Name = "Highlight video link 2")]
    public string? HighlightVideoUrl2 { get; set; }

    [MaxLength(500)]
    [Display(Name = "Website URL")]
    public string? WebsiteUrl { get; set; }

    [MaxLength(100)]
    [Display(Name = "City")]
    public string? City { get; set; }

    [MaxLength(2)]
    [Display(Name = "State")]
    public string? State { get; set; }

    [MaxLength(10)]
    [Display(Name = "ZIP code")]
    public string? ZipCode { get; set; }

    [MaxLength(500)]
    [Display(Name = "Facebook page")]
    public string? FacebookPageUrl { get; set; }

    [MaxLength(500)]
    [Display(Name = "X username")]
    public string? XPageUrl { get; set; }

    [MaxLength(500)]
    [Display(Name = "Instagram username")]
    public string? InstagramUrl { get; set; }

    [MaxLength(500)]
    [Display(Name = "YouTube")]
    public string? YouTubeUrl { get; set; }

    [MaxLength(500)]
    [Display(Name = "TikTok username")]
    public string? TikTokUrl { get; set; }

    [MaxLength(200)]
    [Display(Name = "GameChanger coach")]
    public string? GameChangerCoachName { get; set; }

    [MaxLength(200)]
    [Display(Name = "GameChanger team name")]
    public string? GameChangerTeamName { get; set; }

    public List<Guid> SelectedSportIds { get; set; } = [];

    public IReadOnlyCollection<SportSelectionPageItem> AvailableSports { get; set; } = [];

    public IReadOnlyCollection<string> AvailableGeographicScopeOptions { get; set; } = [];

    public IReadOnlyCollection<string> AvailableTeamRoleOptions { get; set; } = [];
}

public sealed class TeamOpportunityDashboardPageModel
{
    public bool CanPostOpportunities { get; set; }

    public bool HasLimitedPosting { get; set; }

    public bool HasUnlimitedPosting { get; set; }

    public int PublishingLimit { get; set; }

    public int PublishingWindowMonths { get; set; }

    public string PublishingPlanLabel { get; set; } = string.Empty;

    public IReadOnlyCollection<ManagedTeamOpportunitySummaryPageModel> Teams { get; set; } = [];
}

public sealed class ManagedTeamOpportunitySummaryPageModel
{
    public Guid TeamId { get; set; }

    public string TeamName { get; set; } = string.Empty;

    public string? OrganizationName { get; set; }

    public string? LogoImageUrl { get; set; }

    public string Role { get; set; } = string.Empty;

    public string? TeamLevel { get; set; }

    public string? TeamDescription { get; set; }

    public string GeographicScope { get; set; } = string.Empty;

    public string? City { get; set; }

    public string? State { get; set; }

    public string? ZipCode { get; set; }

    public bool IsSearchable { get; set; }

    public bool IsContactInfoVisible { get; set; }

    public int ActiveOpportunityCount { get; set; }

    public int PublishedOpportunityCount { get; set; }

    public int PublishedInWindowCount { get; set; }

    public IReadOnlyCollection<string> Sports { get; set; } = [];
}

public sealed class CoachGettingStartedPageModel
{
    public string FirstName { get; set; } = string.Empty;

    public bool HasTeamRepresentativeRole { get; set; }

    public bool HasTeamBasicOrHigherPlan { get; set; }

    public bool CanPostOpportunities { get; set; }

    public bool CanConfigureTryoutRegistration { get; set; }

    public string TeamPlanLabel { get; set; } = "No team plan";

    public ManagedTeamOpportunitySummaryPageModel? PrimaryTeam { get; set; }

    public CoachGettingStartedOpportunityPageItem? LatestTryout { get; set; }

    public CoachGettingStartedOpportunityPageItem? LatestPickupPlayerListing { get; set; }

    public IReadOnlyCollection<ManagedTeamOpportunitySummaryPageModel> Teams { get; set; } = [];

    public IReadOnlyCollection<CoachGettingStartedStepPageItem> Steps { get; set; } = [];
}

public sealed record CoachGettingStartedStepPageItem(
    int Number,
    string Title,
    string Description,
    string StatusLabel,
    bool IsComplete,
    bool IsAvailable,
    string? ActionLabel,
    string? ActionUrl,
    IReadOnlyCollection<string> Details,
    IReadOnlyCollection<CoachGettingStartedStepLinkPageItem> SecondaryLinks);

public sealed record CoachGettingStartedStepLinkPageItem(
    string Label,
    string Url,
    bool IsAvailable = true);

public sealed record CoachGettingStartedOpportunityPageItem(
    Guid TeamId,
    Guid OpportunityId,
    string Title,
    string Type,
    bool IsPublished,
    bool RegistrationRequired,
    bool HasPdfFlyer,
    int RegistrationCount,
    DateTime UpdatedAt);

public sealed class TeamOpportunityListPageModel
{
    public ManagedTeamOpportunitySummaryPageModel Team { get; set; } = new();

    public bool CanPostOpportunities { get; set; }

    public bool HasLimitedPosting { get; set; }

    public bool HasUnlimitedPosting { get; set; }

    public int PublishingLimit { get; set; }

    public int PublishingWindowMonths { get; set; }

    public string PublishingPlanLabel { get; set; } = string.Empty;

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public int TotalPages { get; set; }

    public bool ShowBasicAnalytics { get; set; }

    public bool ShowDetailedAnalytics { get; set; }

    public int PageViewCountTotal { get; set; }

    public int PageRegistrationCountTotal { get; set; }

    public int PageFavoriteCountTotal { get; set; }

    public decimal? PageViewToRegistrationConversionRate { get; set; }

    public IReadOnlyCollection<TeamOpportunitySummaryPageModel> Opportunities { get; set; } = [];
}

public sealed class TeamOpportunitySummaryPageModel
{
    public Guid OpportunityId { get; set; }

    public Guid TeamId { get; set; }

    public Guid SportId { get; set; }

    public string SportName { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? CompetitionLevel { get; set; }

    public string? AgeGroup { get; set; }

    public decimal RegistrationFee { get; set; }

    public DateTime? RegistrationDeadline { get; set; }

    public DateTime? EventDate { get; set; }

    public DateTime? EventEndDate { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? ZipCode { get; set; }

    public string? WebsiteUrl { get; set; }

    public string? PdfUrl { get; set; }

    public string? UploadedPdfUrl { get; set; }

    public string? UploadedPdfFileName { get; set; }

    public bool IsPublished { get; set; }

    public DateTime? PublishedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public int ViewCount { get; set; }

    public int RegistrationCount { get; set; }

    public int FavoriteCount { get; set; }

    public int UniqueApplicantCount { get; set; }

    public int PendingRegistrationCount { get; set; }

    public int ApprovedRegistrationCount { get; set; }

    public int DeclinedRegistrationCount { get; set; }

    public decimal? ViewToRegistrationConversionRate { get; set; }

    public IReadOnlyCollection<TeamOpportunityRegistrantPageItem> Registrants { get; set; } = [];

    public DateTime UpdatedAt { get; set; }
}

public sealed record TeamOpportunityRegistrantPageItem(
    Guid RegistrationId,
    Guid PlayerId,
    string PlayerName,
    int TryoutNumber,
    int? PlayerAge,
    string? SchoolName,
    string StatusCode,
    string StatusLabel,
    bool IsPresent,
    DateTime? CheckedInAt,
    bool IsWaiverReceived,
    DateTime? WaiverReceivedAt,
    DateTime RegisteredAt,
    bool IsFavoritedByViewer = false,
    string? PlayerPhone = null,
    string? PlayerEmail = null,
    string? GuardianName = null,
    string? GuardianEmail = null,
    string? GuardianPhone = null,
    string? EmergencyContactName = null,
    string? EmergencyContactPhone = null,
    string? MedicalInfo = null,
    string? AdditionalNotes = null);

public sealed class TeamOpportunityRegistrationSharePageModel
{
    public Guid TeamId { get; set; }

    public Guid OpportunityId { get; set; }

    public string TeamName { get; set; } = string.Empty;

    public string? OrganizationName { get; set; }

    public string SportName { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? CompetitionLevel { get; set; }

    public string? AgeGroup { get; set; }

    public DateTime? EventDate { get; set; }

    public DateTime? EventEndDate { get; set; }

    public string? Location { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? ZipCode { get; set; }

    public DateTime GeneratedAt { get; set; }

    public IReadOnlyCollection<TeamOpportunityRegistrantPageItem> Registrants { get; set; } = [];

    public int RegistrantCount => Registrants.Count;
}

public sealed class TeamOpportunityEditorPageModel
{
    public Guid TeamId { get; set; }

    public Guid? OpportunityId { get; set; }

    public bool IsEditMode { get; set; }

    public string TeamName { get; set; } = string.Empty;

    public string? OrganizationName { get; set; }

    public string? TeamDescription { get; set; }

    public bool CanPostOpportunities { get; set; }

    public bool CanConfigureTryoutRegistration { get; set; }

    public bool HasLimitedPosting { get; set; }

    public bool HasUnlimitedPosting { get; set; }

    public int PublishingLimit { get; set; }

    public int PublishingWindowMonths { get; set; }

    public string PublishingPlanLabel { get; set; } = string.Empty;

    public int PublishedInWindowCount { get; set; }

    [Required]
    [MaxLength(50)]
    [Display(Name = "Opportunity type")]
    public string Type { get; set; } = string.Empty;

    [Required]
    [MaxLength(300)]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4000)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Required]
    [Display(Name = "Sport")]
    public Guid SportId { get; set; }

    [MaxLength(100)]
    [Display(Name = "Competition level")]
    public string? CompetitionLevel { get; set; }

    [MaxLength(50)]
    [Display(Name = "Age group")]
    public string? AgeGroup { get; set; }

    [Range(typeof(decimal), "0", "9999999")]
    [Display(Name = "Registration fee")]
    public decimal RegistrationFee { get; set; }

    [Display(Name = "Registration required")]
    public bool RegistrationRequired { get; set; } = true;

    [Display(Name = "Limit registrations to max player count")]
    public bool LimitRegistrationCapacity { get; set; }

    [Range(1, 10000)]
    [Display(Name = "Max registrations")]
    public int? MaxParticipants { get; set; }

    [Display(Name = "Required registration fields")]
    public List<string> RequiredRegistrationFieldCodes { get; set; } = [];

    public IReadOnlyCollection<OpportunityRegistrationFieldOptionPageItem> AvailableRegistrationFieldOptions { get; set; } = [];

    [Display(Name = "Waiver required")]
    public bool WaiverRequired { get; set; }

    [MaxLength(40)]
    [Display(Name = "Waiver method")]
    public string? WaiverMethod { get; set; }

    [Display(Name = "Allow waiver return by email")]
    public bool WaiverReturnByEmail { get; set; }

    [Display(Name = "Allow waiver return at event")]
    public bool WaiverReturnInPerson { get; set; }

    [Display(Name = "Upload waiver PDF")]
    public IFormFile? WaiverPdfUpload { get; set; }

    [Display(Name = "Remove uploaded waiver PDF")]
    public bool RemoveUploadedWaiverPdf { get; set; }

    public bool HasUploadedWaiverPdf { get; set; }

    public string? UploadedWaiverPdfFileName { get; set; }

    public string? UploadedWaiverPdfUrl { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Registration deadline")]
    public DateTime? RegistrationDeadline { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Event start date")]
    public DateTime? EventDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Event end date")]
    public DateTime? EventEndDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Listing start date")]
    public DateTime? ListingStartDate { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Listing end date")]
    public DateTime? ListingEndDate { get; set; }

    [MaxLength(500)]
    [Display(Name = "Location")]
    public string? Location { get; set; }

    [MaxLength(500)]
    [Display(Name = "Address")]
    public string? Address { get; set; }

    [MaxLength(100)]
    [Display(Name = "City")]
    public string? City { get; set; }

    [MaxLength(2)]
    [Display(Name = "State")]
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

    [MaxLength(500)]
    [Display(Name = "Flyer link")]
    public string? PdfUrl { get; set; }

    [Display(Name = "Upload flyer")]
    public IFormFile? PdfUpload { get; set; }

    [Display(Name = "Remove uploaded flyer")]
    public bool RemoveUploadedPdf { get; set; }

    public bool HasUploadedPdf { get; set; }

    public string? UploadedPdfFileName { get; set; }

    public string? UploadedPdfUrl { get; set; }

    [MaxLength(2000)]
    [Display(Name = "Required equipment")]
    public string? RequiredEquipment { get; set; }

    [MaxLength(2000)]
    [Display(Name = "What to bring")]
    public string? WhatToBring { get; set; }

    [MaxLength(2000)]
    [Display(Name = "Special instructions")]
    public string? SpecialInstructions { get; set; }

    [Display(Name = "Publish now")]
    public bool IsPublished { get; set; } = true;

    [DataType(DataType.Date)]
    [Display(Name = "Expires on")]
    public DateTime? ExpiresAt { get; set; }

    public IReadOnlyCollection<SportSelectionPageItem> AvailableSports { get; set; } = [];

    public IReadOnlyCollection<string> AvailableOpportunityTypes { get; set; } = [];
}

public sealed class PlayerListingListPageModel
{
    public bool CanCreateListings { get; set; }

    public int Page { get; set; }

    public int PageSize { get; set; }

    public int TotalCount { get; set; }

    public int TotalPages { get; set; }

    public IReadOnlyCollection<ManagedPlayerSelectionPageItem> ManagedPlayers { get; set; } = [];

    public IReadOnlyCollection<PlayerListingSummaryPageModel> Listings { get; set; } = [];
}

public sealed class PlayerListingSummaryPageModel
{
    public Guid ListingId { get; set; }

    public string ListingType { get; set; } = string.Empty;

    public string ListingTypeLabel { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public Guid? PlayerId { get; set; }

    public string? PlayerName { get; set; }

    public string? SportName { get; set; }

    public decimal? AskingPrice { get; set; }

    public string? Currency { get; set; }

    public string? Condition { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? ZipCode { get; set; }

    public string? UploadedPdfUrl { get; set; }

    public string? UploadedPdfFileName { get; set; }

    public bool IsPublished { get; set; }

    public bool IsSearchable { get; set; }

    public DateTime? PublishedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public sealed class PlayerListingEditorPageModel
{
    public Guid? ListingId { get; set; }

    public bool IsEditMode { get; set; }

    [Required]
    [MaxLength(50)]
    [Display(Name = "Listing type")]
    public string ListingType { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    [Display(Name = "Title")]
    public string Title { get; set; } = string.Empty;

    [MaxLength(4000)]
    [Display(Name = "Description")]
    public string? Description { get; set; }

    [Display(Name = "Player profile")]
    public Guid? PlayerId { get; set; }

    [Display(Name = "Sport")]
    public Guid? SportId { get; set; }

    [Range(typeof(decimal), "0", "9999999")]
    [Display(Name = "Asking price")]
    public decimal? AskingPrice { get; set; }

    [MaxLength(3)]
    [Display(Name = "Currency")]
    public string? Currency { get; set; }

    [MaxLength(50)]
    [Display(Name = "Condition")]
    public string? Condition { get; set; }

    [MaxLength(100)]
    [Display(Name = "City")]
    public string? City { get; set; }

    [MaxLength(2)]
    [Display(Name = "State")]
    public string? State { get; set; }

    [MaxLength(10)]
    [Display(Name = "ZIP code")]
    public string? ZipCode { get; set; }

    [Display(Name = "Allow this listing in search")]
    public bool IsSearchable { get; set; } = true;

    [Display(Name = "Publish now")]
    public bool IsPublished { get; set; } = true;

    [DataType(DataType.Date)]
    [Display(Name = "Expires on")]
    public DateTime? ExpiresAt { get; set; }

    [Display(Name = "Upload PDF flyer")]
    public IFormFile? PdfUpload { get; set; }

    [Display(Name = "Remove uploaded PDF")]
    public bool RemoveUploadedPdf { get; set; }

    public bool HasUploadedPdf { get; set; }

    public string? UploadedPdfFileName { get; set; }

    public string? UploadedPdfUrl { get; set; }

    [Display(Name = "Show selected profile links")]
    public List<string> VisibleSocialLinkKeys { get; set; } = [];

    public IReadOnlyCollection<PlayerListingTypeSelectionPageItem> AvailableListingTypes { get; set; } = [];

    public IReadOnlyCollection<ManagedPlayerSelectionPageItem> AvailablePlayers { get; set; } = [];

    public IReadOnlyCollection<SportSelectionPageItem> AvailableSports { get; set; } = [];

    public IReadOnlyCollection<PlayerListingVisibilityOptionPageItem> AvailableSocialDisplayOptions { get; set; } = [];
}

public sealed class PlayerListingDetailPageModel
{
    public Guid ListingId { get; set; }

    public string ListingTypeLabel { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? SportName { get; set; }

    public decimal? AskingPrice { get; set; }

    public string? Currency { get; set; }

    public string? Condition { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? ZipCode { get; set; }

    public DateTime? PublishedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public string? PlayerName { get; set; }

    public Guid? PlayerId { get; set; }

    public string? PublicPlayerProfileUrl { get; set; }

    public string? ProfileImageUrl { get; set; }

    public DateTime? PlayerDateOfBirth { get; set; }

    public string? PlayerCity { get; set; }

    public string? PlayerState { get; set; }

    public string? PlayerZipCode { get; set; }

    public string? SchoolName { get; set; }

    public string? CurrentTeamName { get; set; }

    public int? GraduationYear { get; set; }

    public string? Height { get; set; }

    public string? Weight { get; set; }

    public string? ThrowsHand { get; set; }

    public string? BatsHand { get; set; }

    public string? SixtyYardDash { get; set; }

    public string? HomeToFirstTime { get; set; }

    public string? ExitVelocity { get; set; }

    public string? ThrowingVelocity { get; set; }

    public string? PitchVelocity { get; set; }

    public string? CatcherPopTime { get; set; }

    public string? AdditionalMetrics { get; set; }

    public bool CanViewContactDetails { get; set; }

    public string? ContactEmail { get; set; }

    public string? ContactPhone { get; set; }

    public IReadOnlyCollection<PlayerListingSportSummaryPageItem> Sports { get; set; } = [];

    public IReadOnlyCollection<ExternalProfileLinkPageItem> SocialLinks { get; set; } = [];

    public IReadOnlyCollection<ExternalProfileLinkPageItem> ProfileVideoLinks { get; set; } = [];

    public IReadOnlyCollection<ExternalProfileLinkPageItem> RecruitingLinks { get; set; } = [];

    public string? PdfUrl { get; set; }

    public string? PdfFileName { get; set; }

    public bool ViewerIsAuthenticated { get; set; }

    public bool IsFavorited { get; set; }

    public bool ViewerCanReport { get; set; }

    public bool ViewerOwnsListing { get; set; }

    public bool ViewerHasOpenReport { get; set; }
}

public sealed class PlayerProfileDetailPageModel
{
    public Guid PlayerId { get; set; }

    public string PlayerName { get; set; } = string.Empty;

    public string? ProfileImageUrl { get; set; }

    public DateTime DateOfBirth { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? ZipCode { get; set; }

    public string? SchoolName { get; set; }

    public string? CurrentTeamName { get; set; }

    public int? GraduationYear { get; set; }

    public string? Height { get; set; }

    public string? Weight { get; set; }

    public string? ThrowsHand { get; set; }

    public string? BatsHand { get; set; }

    public string? SixtyYardDash { get; set; }

    public string? HomeToFirstTime { get; set; }

    public string? ExitVelocity { get; set; }

    public string? ThrowingVelocity { get; set; }

    public string? PitchVelocity { get; set; }

    public string? CatcherPopTime { get; set; }

    public string? AdditionalMetrics { get; set; }

    public bool CanViewContactDetails { get; set; }

    public string? ContactEmail { get; set; }

    public string? ContactPhone { get; set; }

    public IReadOnlyCollection<PlayerListingSportSummaryPageItem> Sports { get; set; } = [];

    public IReadOnlyCollection<ExternalProfileLinkPageItem> SocialLinks { get; set; } = [];

    public IReadOnlyCollection<ExternalProfileLinkPageItem> ProfileVideoLinks { get; set; } = [];

    public IReadOnlyCollection<ExternalProfileLinkPageItem> RecruitingLinks { get; set; } = [];

    public IReadOnlyCollection<PlayerProfileListingSummaryPageItem> ActiveListings { get; set; } = [];

    public bool ViewerIsAuthenticated { get; set; }
}

public sealed record PlayerProfileListingSummaryPageItem(
    Guid ListingId,
    string ListingTypeLabel,
    string Title,
    string? Description,
    string? SportName,
    decimal? AskingPrice,
    string? Currency,
    string? City,
    string? State,
    string? ZipCode,
    DateTime? PublishedAt,
    DateTime? ExpiresAt);

public sealed class TeamOpportunityDetailPageModel
{
    public Guid OpportunityId { get; set; }

    public Guid TeamId { get; set; }

    public string TeamName { get; set; } = string.Empty;

    public string? TeamLogoImageUrl { get; set; }

    public string? OrganizationName { get; set; }

    public string? TeamDescription { get; set; }

    public string SportName { get; set; } = string.Empty;

    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? CompetitionLevel { get; set; }

    public string? AgeGroup { get; set; }

    public bool RegistrationRequired { get; set; } = true;

    public decimal RegistrationFee { get; set; }

    public int? MaxParticipants { get; set; }

    public int ActiveRegistrationCount { get; set; }

    public int? RemainingRegistrationSpots { get; set; }

    public DateTime? RegistrationDeadline { get; set; }

    public DateTime? EventDate { get; set; }

    public DateTime? EventEndDate { get; set; }

    public DateTime? PublishedAt { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public string? Location { get; set; }

    public string? Address { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? ZipCode { get; set; }

    public bool IsContactInfoVisible { get; set; }

    public string? ContactEmail { get; set; }

    public string? ContactPhone { get; set; }

    public string? WebsiteUrl { get; set; }

    public string? PdfUrl { get; set; }

    public bool IsFlyerImage { get; set; }

    public string? RequiredEquipment { get; set; }

    public string? WhatToBring { get; set; }

    public string? SpecialInstructions { get; set; }

    public bool ViewerIsAuthenticated { get; set; }

    public bool ViewerCanSubmitRegistration { get; set; }

    public bool IsRegistrationOpen { get; set; }

    public string? RegistrationClosedReason { get; set; }

    public bool WaiverRequired { get; set; }

    public string? WaiverMethod { get; set; }

    public bool WaiverReturnByEmail { get; set; }

    public bool WaiverReturnInPerson { get; set; }

    public string? WaiverPdfUrl { get; set; }

    public string? WaiverReturnEmail { get; set; }

    public IReadOnlyCollection<OpportunityRegistrationFieldOptionPageItem> RequiredRegistrationFields { get; set; } = [];

    public IReadOnlyCollection<OpportunityRegistrationPlayerOptionPageItem> RegistrationPlayers { get; set; } = [];

    public TeamOpportunityRegistrationInputPageModel RegistrationForm { get; set; } = new();

    public bool IsFavorited { get; set; }

    public bool ViewerCanReport { get; set; }

    public bool ViewerManagesTeam { get; set; }

    public bool ViewerHasOpenReport { get; set; }
}

public sealed class ListingReportPageModel
{
    public string TargetTypeLabel { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string? Subtitle { get; set; }

    public string ReturnUrl { get; set; } = "/";

    public string PostUrl { get; set; } = string.Empty;

    public bool ViewerCanReport { get; set; }

    public string? BlockMessage { get; set; }

    public string? Reason { get; set; }

    public string? Details { get; set; }

    public IReadOnlyCollection<ListingReportReasonOptionPageItem> ReasonOptions { get; set; } = [];
}

public sealed class SearchTeamItemsPageModel
{
    [MaxLength(200)]
    [Display(Name = "Keywords")]
    public string? Q { get; set; }

    [Display(Name = "Sport")]
    public Guid? SportId { get; set; }

    [MaxLength(50)]
    [Display(Name = "Type")]
    public string? Type { get; set; } = "all";

    [MaxLength(50)]
    [Display(Name = "Age group")]
    public string? AgeGroup { get; set; }

    [MaxLength(100)]
    [Display(Name = "Competition level")]
    public string? CompetitionLevel { get; set; }

    [MaxLength(10)]
    [Display(Name = "ZIP code")]
    public string? OriginZipCode { get; set; }

    [Range(1, 250)]
    [Display(Name = "Radius")]
    public int? RadiusMiles { get; set; } = 25;

    [MaxLength(100)]
    [Display(Name = "City")]
    public string? City { get; set; }

    [MaxLength(2)]
    [Display(Name = "State")]
    public string? State { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "From")]
    public DateTime? EventDateFrom { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "To")]
    public DateTime? EventDateTo { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public int TotalCount { get; set; }

    public int TotalPages { get; set; } = 1;

    public bool CanSearchTeamItems { get; set; }

    public bool HasAdvancedOpportunitySearch { get; set; }

    public bool CanUseOpportunityTypeFilters { get; set; }

    public bool CanUseCompetitionLevelFilter { get; set; }

    public bool CanUseExpandedRadius { get; set; }

    public bool TypeFilterConstrained { get; set; }

    public bool CompetitionLevelFilterIgnored { get; set; }

    public bool RadiusWasConstrained { get; set; }

    public int MaxRadiusMiles { get; set; } = 120;

    public int? SearchRadiusMiles { get; set; }

    public string? SearchOriginZipCode { get; set; }

    public IReadOnlyCollection<SportSelectionPageItem> AvailableSports { get; set; } = [];

    public IReadOnlyCollection<SearchFilterOptionPageItem> AvailableOpportunityTypes { get; set; } = [];

    public IReadOnlyCollection<SearchRadiusOptionPageItem> AvailableRadiusOptions { get; set; } = [];

    public IReadOnlyCollection<TeamItemSearchResultPageItem> Results { get; set; } = [];

    public IReadOnlyCollection<TeamItemSearchSuggestionGroupPageItem> RadiusSuggestions { get; set; } = [];

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;

    public int FirstItemNumber => TotalCount == 0 ? 0 : ((Page - 1) * PageSize) + 1;

    public int LastItemNumber => Math.Min(Page * PageSize, TotalCount);
}

public sealed class SearchPlayersPageModel
{
    [MaxLength(200)]
    [Display(Name = "Keywords")]
    public string? Q { get; set; }

    [MaxLength(50)]
    [Display(Name = "Listing type")]
    public string? ListingType { get; set; } = "all";

    [Display(Name = "Sport")]
    public Guid? SportId { get; set; }

    [Range(0, 100)]
    [Display(Name = "Min age")]
    public int? MinAge { get; set; }

    [Range(0, 100)]
    [Display(Name = "Max age")]
    public int? MaxAge { get; set; }

    [MaxLength(50)]
    [Display(Name = "Skill level")]
    public string? SkillLevel { get; set; }

    [MaxLength(10)]
    [Display(Name = "ZIP code")]
    public string? OriginZipCode { get; set; }

    [Range(1, 250)]
    [Display(Name = "Radius")]
    public int? RadiusMiles { get; set; } = 25;

    [MaxLength(100)]
    [Display(Name = "City")]
    public string? City { get; set; }

    [MaxLength(2)]
    [Display(Name = "State")]
    public string? State { get; set; }

    [Range(typeof(decimal), "0", "9999999")]
    [Display(Name = "Min price")]
    public decimal? MinPrice { get; set; }

    [Range(typeof(decimal), "0", "9999999")]
    [Display(Name = "Max price")]
    public decimal? MaxPrice { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;

    public int TotalCount { get; set; }

    public int TotalPages { get; set; } = 1;

    public bool CanSearchPlayers { get; set; }

    public bool HasAdvancedPlayerSearch { get; set; }

    public bool CanUseSkillLevelFilter { get; set; }

    public bool CanUseExpandedRadius { get; set; }

    public bool SkillLevelFilterIgnored { get; set; }

    public bool RadiusWasConstrained { get; set; }

    public int MaxRadiusMiles { get; set; } = 120;

    public int? SearchRadiusMiles { get; set; }

    public string? SearchOriginZipCode { get; set; }

    public IReadOnlyCollection<SportSelectionPageItem> AvailableSports { get; set; } = [];

    public IReadOnlyCollection<SearchFilterOptionPageItem> AvailableListingTypes { get; set; } = [];

    public IReadOnlyCollection<SearchRadiusOptionPageItem> AvailableRadiusOptions { get; set; } = [];

    public IReadOnlyCollection<PlayerSearchResultPageItem> Results { get; set; } = [];

    public IReadOnlyCollection<PlayerSearchSuggestionGroupPageItem> RadiusSuggestions { get; set; } = [];

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;

    public int FirstItemNumber => TotalCount == 0 ? 0 : ((Page - 1) * PageSize) + 1;

    public int LastItemNumber => Math.Min(Page * PageSize, TotalCount);
}

public sealed record SearchFilterOptionPageItem(
    string Code,
    string Label,
    bool IsSelected,
    bool IsEnabled,
    string? DisabledReason = null);

public sealed record SearchRadiusOptionPageItem(
    int Miles,
    string Label,
    bool IsSelected,
    bool IsEnabled);

public sealed record TeamItemSearchResultPageItem(
    Guid OpportunityId,
    Guid TeamId,
    string Title,
    string Type,
    string TypeLabel,
    string TeamName,
    string? OrganizationName,
    string SportName,
    string? Description,
    string? CompetitionLevel,
    string? AgeGroup,
    decimal RegistrationFee,
    DateTime? RegistrationDeadline,
    DateTime? EventDate,
    DateTime? EventEndDate,
    string? City,
    string? State,
    string? ZipCode,
    double? DistanceMiles,
    bool IsFavorited,
    int RelevanceScore);

public sealed record TeamItemSearchSuggestionGroupPageItem(
    int RadiusMiles,
    IReadOnlyCollection<TeamItemSearchResultPageItem> Results);

public sealed record PlayerSearchResultPageItem(
    Guid ListingId,
    string ListingType,
    string ListingTypeLabel,
    string Title,
    string? Description,
    Guid? PlayerId,
    string? PlayerName,
    int? PlayerAge,
    DateTime? PlayerDateOfBirth,
    string? SchoolName,
    string? CurrentTeamName,
    int? GraduationYear,
    string? Height,
    string? Weight,
    string? ThrowsHand,
    string? BatsHand,
    string? SportName,
    decimal? AskingPrice,
    string? Currency,
    string? Condition,
    string? City,
    string? State,
    string? ZipCode,
    double? DistanceMiles,
    bool IsPriorityListing,
    bool IsFavorited,
    int RelevanceScore);

public sealed record PlayerSearchSuggestionGroupPageItem(
    int RadiusMiles,
    IReadOnlyCollection<PlayerSearchResultPageItem> Results);

public sealed record DashboardPlayerListingFavoritePageItem(
    Guid ListingId,
    string ListingTypeLabel,
    string Title,
    string? PlayerName,
    string? SportName,
    string? City,
    string? State,
    string? ZipCode,
    bool IsAvailable,
    DateTime CreatedAt);

public sealed record DashboardOpportunityFavoritePageItem(
    Guid OpportunityId,
    string TypeLabel,
    string Title,
    string TeamName,
    string? OrganizationName,
    string SportName,
    DateTime? EventDate,
    string? City,
    string? State,
    string? ZipCode,
    bool IsAvailable,
    DateTime CreatedAt);

public sealed class TeamOpportunityRegistrationInputPageModel
{
    [Display(Name = "Player profile")]
    public Guid PlayerId { get; set; }

    [MaxLength(200)]
    [Display(Name = "Player name")]
    public string? PlayerName { get; set; }

    [DataType(DataType.Date)]
    [Display(Name = "Player birthdate")]
    public DateTime? PlayerBirthDate { get; set; }

    [MaxLength(200)]
    [Display(Name = "Player school")]
    public string? PlayerSchool { get; set; }

    [Phone]
    [MaxLength(20)]
    [Display(Name = "Player cell phone")]
    public string? PlayerPhone { get; set; }

    [EmailAddress]
    [MaxLength(255)]
    [Display(Name = "Player email address")]
    public string? PlayerEmail { get; set; }

    [MaxLength(200)]
    [Display(Name = "Guardian name")]
    public string? GuardianName { get; set; }

    [EmailAddress]
    [MaxLength(255)]
    [Display(Name = "Guardian email")]
    public string? GuardianEmail { get; set; }

    [Phone]
    [MaxLength(20)]
    [Display(Name = "Guardian phone")]
    public string? GuardianPhone { get; set; }

    [MaxLength(200)]
    [Display(Name = "Emergency contact name")]
    public string? EmergencyContactName { get; set; }

    [Phone]
    [MaxLength(20)]
    [Display(Name = "Emergency contact phone")]
    public string? EmergencyContactPhone { get; set; }

    [MaxLength(4000)]
    [Display(Name = "Medical notes")]
    public string? MedicalInfo { get; set; }

    [Display(Name = "Waiver acknowledgment")]
    public bool WaiverAcknowledged { get; set; }

    [MaxLength(200)]
    [Display(Name = "Waiver signer name")]
    public string? WaiverSignerName { get; set; }

    [MaxLength(2000)]
    [Display(Name = "Additional notes")]
    public string? AdditionalNotes { get; set; }
}

public sealed record OpportunityRegistrationFieldOptionPageItem(
    string Code,
    string Label,
    string Description,
    bool IsRequired);

public sealed record OpportunityRegistrationPlayerOptionPageItem(
    Guid PlayerId,
    string DisplayName,
    bool AlreadyRegistered,
    string? ProfileName,
    DateTime? ProfileBirthDate,
    string? ProfileSchool,
    string? ProfilePhone,
    string? ProfileEmail);

public sealed record ListingReportReasonOptionPageItem(
    string Value,
    string Label);

public sealed class AccountSettingsPageModel
{
    public ProfileSettingsPageModel Profile { get; set; } = new();

    public EmailSettingsPageModel Email { get; set; } = new();

    public PhoneSettingsPageModel Phone { get; set; } = new();

    public PasswordSettingsPageModel Password { get; set; } = new();

    public AccountTypeSettingsPageModel AccountTypes { get; set; } = new();

    public SmsConsentSettingsPageModel SmsConsent { get; set; } = new();

    public IReadOnlyCollection<BillingPlanResponse> RecommendedPlans { get; set; } = [];

    public string? CurrentPlanName { get; set; }

    public string? CurrentPlanStatus { get; set; }

    public IReadOnlyCollection<string> FeatureCodes { get; set; } = [];

    public bool StripeCheckoutConfigured { get; set; }

    public bool HasStripeCustomer { get; set; }

    public bool HasPendingPaidPlanSelection { get; set; }

    public IReadOnlyCollection<AccountMembershipSummaryItem> MembershipSummaries { get; set; } = [];

    public bool CanCancelPaidMembership { get; set; }

    public bool CanCancelPlayerParentPaidMembership { get; set; }

    public bool CanCancelTeamPaidMembership { get; set; }

    public bool HasScheduledPaidCancellation { get; set; }

    public DateTime? ScheduledPaidCancellationAt { get; set; }

    public DateTime? PlayerParentScheduledPaidCancellationAt { get; set; }

    public DateTime? TeamScheduledPaidCancellationAt { get; set; }

    public DashboardActivityPreferencesResponse? DashboardActivityPreferences { get; set; }

    public IReadOnlyCollection<FlyerTeamClaimPageItem> ClaimableFlyerTeams { get; set; } = [];
}

public sealed class ProfileSettingsPageModel
{
    [Required]
    [MaxLength(100)]
    [Display(Name = "First name")]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    [Display(Name = "Last name")]
    public string LastName { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    [Display(Name = "Date of birth")]
    public DateTime? DateOfBirth { get; set; }

    [MaxLength(10)]
    [Display(Name = "ZIP code")]
    public string? ZipCode { get; set; }

    [MaxLength(100)]
    public string? City { get; set; }

    [MaxLength(2)]
    public string? State { get; set; }
}

public sealed class EmailSettingsPageModel
{
    [Display(Name = "Current email")]
    public string CurrentEmail { get; set; } = string.Empty;

    public bool EmailConfirmed { get; set; }

    public bool HasLocalPassword { get; set; }

    [Required]
    [EmailAddress]
    [MaxLength(255)]
    [Display(Name = "New email address")]
    public string NewEmail { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string? CurrentPassword { get; set; }
}

public sealed class PhoneSettingsPageModel
{
    [Phone]
    [MaxLength(20)]
    [Display(Name = "Phone number")]
    public string? PhoneNumber { get; set; }

    public bool PhoneNumberConfirmed { get; set; }

    [MaxLength(20)]
    [Display(Name = "Verification code")]
    public string? VerificationCode { get; set; }

    [MaxLength(2048)]
    public string? ReturnUrl { get; set; }
}

public sealed class PasswordSettingsPageModel
{
    public bool HasLocalPassword { get; set; }

    [DataType(DataType.Password)]
    [Display(Name = "Current password")]
    public string? CurrentPassword { get; set; }

    [Required]
    [MinLength(8)]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required]
    [Compare(nameof(NewPassword))]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm new password")]
    public string ConfirmNewPassword { get; set; } = string.Empty;
}

public sealed class AccountTypeSettingsPageModel
{
    public List<string> AccountTypes { get; set; } = [];

    public IReadOnlyCollection<AccountTypeSelectionItem> AvailableAccountTypes { get; set; } = [];
}

public sealed class SmsConsentSettingsPageModel
{
    [Display(Name = "SMS consent")]
    public bool SmsConsentAccepted { get; set; }

    public DateTime? SmsConsentAcceptedAt { get; set; }

    [Phone]
    [MaxLength(20)]
    [Display(Name = "Phone number")]
    public string? PhoneNumber { get; set; }
}

public sealed record AccountMembershipSummaryItem(
    string PlanCode,
    string BundleType,
    string PlanName,
    string Status,
    string BillingInterval,
    string Price,
    string Scope,
    bool HasActiveEntitlement,
    DateTime? CurrentPeriodStart,
    DateTime? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    DateTime UpdatedAt)
{
    public string? CurrentPeriodEndLabel { get; init; }

    public string? ActiveEntitlementLabel { get; init; }
}

public sealed record FlyerTeamClaimPageItem(
    Guid TeamId,
    string TeamName,
    string? TeamLevel,
    string? SportName,
    string? City,
    string? State,
    string? ZipCode,
    string MatchedBy,
    int ListingCount,
    DateTime LatestFlyerAt);

public sealed record AccountTypeSelectionItem(
    string Name,
    string Label,
    string Description,
    bool IsCommonFirstChoice,
    bool IsSelected);

public sealed record LaunchPromotionPageItem(
    string PromotionName,
    int GrantMonths,
    int RemainingCount,
    bool CanClaim,
    bool RequiresEmailConfirmation,
    bool HasAlreadyClaimed,
    DateTime? ExistingClaimEndsAtUtc,
    IReadOnlyCollection<string> PlanNames);

public sealed record OnboardingStepPageItem(
    string Code,
    string Title,
    string Description,
    bool IsRequired,
    bool IsComplete);

public sealed record OnboardingTryoutRegistrationPageItem(
    Guid RegistrationId,
    Guid OpportunityId,
    Guid PlayerId,
    string PlayerName,
    string OpportunityTitle,
    string TeamName,
    string SportName,
    string StatusLabel,
    DateTime? EventDate,
    DateTime? EventEndDate,
    DateTime? RegistrationDeadline,
    DateTime RegisteredAt,
    string? City,
    string? State);

public sealed record SportSelectionPageItem(
    Guid Id,
    string Name,
    bool IsSelected);

public sealed record PlayerListingTypeSelectionPageItem(
    string Code,
    string Label,
    string Description,
    bool RequiresPlayerSelection,
    bool RequiresSportSelection,
    bool SupportsCondition,
    bool SupportsAskingPrice);

public sealed record ManagedPlayerSelectionPageItem(
    Guid PlayerId,
    string DisplayName,
    string? City,
    string? State,
    string? ZipCode);

public sealed record PlayerListingSportSummaryPageItem(
    string SportName,
    string? SkillLevel,
    string? PrimaryPosition,
    string? SecondaryPositions,
    string? ExperienceLevel,
    int? YearsPlaying,
    string? Availability);

public sealed record ExternalProfileLinkPageItem(
    string Label,
    string Url);

public sealed record PlayerListingVisibilityOptionPageItem(
    string Key,
    string Label);

public sealed class PlayerSportDetailPageModel
{
    public Guid SportId { get; set; }

    public string SportName { get; set; } = string.Empty;

    [Display(Name = "Participates")]
    public bool IsSelected { get; set; }

    [MaxLength(50)]
    [Display(Name = "Ability level")]
    public string? SkillLevel { get; set; }

    [MaxLength(100)]
    [Display(Name = "Primary position")]
    public string? PrimaryPosition { get; set; }

    [MaxLength(200)]
    [Display(Name = "Secondary positions")]
    public string? SecondaryPositions { get; set; }

    [MaxLength(50)]
    [Display(Name = "Experience level")]
    public string? ExperienceLevel { get; set; }

    [Range(0, 100)]
    [Display(Name = "Years playing")]
    public int? YearsPlaying { get; set; }

    [MaxLength(500)]
    [Display(Name = "Availability")]
    public string? Availability { get; set; }
}
