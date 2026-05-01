using System.ComponentModel.DataAnnotations;

namespace TryOutSpot.Web.Models.Account;

/// <summary>
/// Request to complete password reset with a token.
/// </summary>
public sealed class ResetPasswordRequest
{
    /// <summary>
    /// Email address for the account.
    /// </summary>
    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// ASP.NET Core Identity password reset token.
    /// </summary>
    [Required]
    public string Token { get; set; } = string.Empty;

    /// <summary>
    /// New password. Must satisfy ASP.NET Core Identity password rules.
    /// </summary>
    [Required]
    [MinLength(8)]
    public string NewPassword { get; set; } = string.Empty;
}
