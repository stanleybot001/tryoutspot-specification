using System.ComponentModel.DataAnnotations;

namespace TryOutSpot.Web.Models.Account;

/// <summary>
/// Request to soft delete an account after password verification.
/// </summary>
public sealed class DeleteAccountRequest
{
    /// <summary>
    /// Email address for the account being deleted.
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

    /// <summary>
    /// Optional user-provided reason for deleting the account.
    /// </summary>
    [MaxLength(500)]
    public string? Reason { get; set; }
}
