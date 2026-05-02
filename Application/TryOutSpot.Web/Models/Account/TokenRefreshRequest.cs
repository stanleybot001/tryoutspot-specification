using System.ComponentModel.DataAnnotations;

namespace TryOutSpot.Web.Models.Account;

/// <summary>
/// Request to exchange a valid refresh token for a new token pair.
/// </summary>
public sealed class TokenRefreshRequest
{
    /// <summary>
    /// Refresh token previously returned by login or refresh.
    /// </summary>
    [Required]
    public string RefreshToken { get; set; } = string.Empty;
}
