using System.ComponentModel.DataAnnotations;

namespace TryOutSpot.Web.Models.Account;

/// <summary>
/// Request to verify an account email address.
/// </summary>
public sealed class VerifyEmailRequest
{
    /// <summary>
    /// Email address for the account being verified.
    /// </summary>
    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// ASP.NET Core Identity email confirmation token.
    /// </summary>
    [Required]
    public string Token { get; set; } = string.Empty;
}
