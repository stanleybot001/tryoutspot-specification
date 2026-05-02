using System.ComponentModel.DataAnnotations;

namespace TryOutSpot.Web.Models.Account;

/// <summary>
/// Request to send a phone verification code for the authenticated account.
/// </summary>
public sealed class SendPhoneVerificationRequest
{
    /// <summary>
    /// Optional phone number to store before sending the verification code.
    /// </summary>
    [Phone]
    [MaxLength(20)]
    public string? PhoneNumber { get; set; }
}
