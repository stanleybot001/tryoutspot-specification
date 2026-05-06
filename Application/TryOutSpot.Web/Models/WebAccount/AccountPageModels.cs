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
