using System.ComponentModel.DataAnnotations;
using TryOutSpot.Web.Models.Billing;

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

    public bool GoogleIsConfigured { get; set; }

    public bool FacebookIsConfigured { get; set; }

    public bool AppleIsConfigured { get; set; }
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

    public List<string> AccountTypes { get; set; } = [];

    public IReadOnlyCollection<AccountTypeSelectionItem> AvailableAccountTypes { get; set; } = [];

    public IReadOnlyCollection<BillingPlanResponse> RecommendedPlans { get; set; } = [];

    public IReadOnlyCollection<OnboardingStepPageItem> Steps { get; set; } = [];

    public IReadOnlyCollection<string> FeatureCodes { get; set; } = [];
}

public sealed class AddPlayerProfilePageModel
{
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

    [EmailAddress]
    [MaxLength(255)]
    [Display(Name = "Contact email")]
    public string? ContactEmail { get; set; }

    [Phone]
    [MaxLength(20)]
    [Display(Name = "Contact phone")]
    public string? ContactPhone { get; set; }

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

    public List<Guid> SelectedSportIds { get; set; } = [];

    public IReadOnlyCollection<SportSelectionPageItem> AvailableSports { get; set; } = [];

    public IReadOnlyCollection<string> AvailableRelationshipOptions { get; set; } = [];
}

public sealed class ChoosePlanPageModel
{
    [Required]
    [Display(Name = "Plan")]
    public string PlanCode { get; set; } = string.Empty;

    [Required]
    [Display(Name = "Billing interval")]
    public string BillingInterval { get; set; } = "month";

    public bool StripeIsConfigured { get; set; }

    public bool CheckoutAvailableForSelection { get; set; }

    public IReadOnlyCollection<BillingPlanResponse> AvailablePlans { get; set; } = [];
}

public sealed class AddTeamOrOrganizationPageModel
{
    [Required]
    [Display(Name = "Create")]
    public string CreateType { get; set; } = "team";

    [Required]
    [MaxLength(200)]
    [Display(Name = "Team name")]
    public string TeamName { get; set; } = string.Empty;

    [MaxLength(200)]
    [Display(Name = "Organization name")]
    public string? OrganizationName { get; set; }

    [Required]
    [MaxLength(50)]
    [Display(Name = "Your team role")]
    public string TeamRole { get; set; } = string.Empty;

    [MaxLength(50)]
    [Display(Name = "Team level")]
    public string? TeamLevel { get; set; }

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

    public List<Guid> SelectedSportIds { get; set; } = [];

    public IReadOnlyCollection<SportSelectionPageItem> AvailableSports { get; set; } = [];

    public IReadOnlyCollection<string> AvailableTeamRoleOptions { get; set; } = [];
}

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

public sealed record AccountTypeSelectionItem(
    string Name,
    string Label,
    string Description,
    bool IsCommonFirstChoice,
    bool IsSelected);

public sealed record OnboardingStepPageItem(
    string Code,
    string Title,
    string Description,
    bool IsRequired,
    bool IsComplete);

public sealed record SportSelectionPageItem(
    Guid Id,
    string Name,
    bool IsSelected);
