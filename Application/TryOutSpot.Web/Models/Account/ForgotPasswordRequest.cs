using System.ComponentModel.DataAnnotations;

namespace TryOutSpot.Web.Models.Account;

/// <summary>
/// Request to begin password reset for an account email address.
/// </summary>
public sealed class ForgotPasswordRequest
{
    /// <summary>
    /// Email address for the account.
    /// </summary>
    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;
}
