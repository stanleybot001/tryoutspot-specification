using System.ComponentModel.DataAnnotations;

namespace TryOutSpot.Web.Models.Account;

/// <summary>
/// Request to resend account email verification instructions.
/// </summary>
public sealed class ResendEmailVerificationRequest
{
    /// <summary>
    /// Email address for the account.
    /// </summary>
    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;
}
