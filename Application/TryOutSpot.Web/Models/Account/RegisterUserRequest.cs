using System.ComponentModel.DataAnnotations;

namespace TryOutSpot.Web.Models.Account;

/// <summary>
/// Request to create a new TryOutSpot user account.
/// </summary>
public sealed class RegisterUserRequest
{
    /// <summary>
    /// Primary email address and login username.
    /// </summary>
    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Initial password. Must satisfy ASP.NET Core Identity password rules.
    /// </summary>
    [Required]
    [MinLength(8)]
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// User first name.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// User last name.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Optional phone number for account contact and future verification.
    /// </summary>
    [Phone]
    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Optional consent to receive transactional SMS messages from TryOutSpot.
    /// </summary>
    public bool SmsConsentAccepted { get; set; }

    /// <summary>
    /// Optional date of birth for age-sensitive player workflows.
    /// </summary>
    public DateTime? DateOfBirth { get; set; }

    /// <summary>
    /// Optional postal code used for location-based opportunity matching.
    /// </summary>
    [MaxLength(10)]
    public string? ZipCode { get; set; }

    /// <summary>
    /// Optional city.
    /// </summary>
    [MaxLength(100)]
    public string? City { get; set; }

    /// <summary>
    /// Optional two-letter state code.
    /// </summary>
    [MaxLength(2)]
    public string? State { get; set; }

    /// <summary>
    /// One or more public account types, such as Parent, Player, Coach, TeamManager, AcademyDirector, or OrganizationAdmin.
    /// </summary>
    public IReadOnlyCollection<string> AccountTypes { get; set; } = [];
}
