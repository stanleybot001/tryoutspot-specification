using System.ComponentModel.DataAnnotations;

namespace TryOutSpot.Web.Models.Account;

/// <summary>
/// Request to authenticate an active, verified account.
/// </summary>
public sealed class LoginRequest
{
    /// <summary>
    /// Primary account email address.
    /// </summary>
    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Current account password.
    /// </summary>
    [Required]
    public string Password { get; set; } = string.Empty;
}
