using System.ComponentModel.DataAnnotations;

namespace TryOutSpot.Web.Models.Account;

/// <summary>
/// Request to verify the authenticated account phone number.
/// </summary>
public sealed class VerifyPhoneRequest
{
    /// <summary>
    /// Phone number being verified. If omitted, the current account phone number is used.
    /// </summary>
    [Phone]
    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Verification code delivered by the configured phone sender.
    /// </summary>
    [Required]
    public string Code { get; set; } = string.Empty;
}
