namespace TryOutSpot.Web.Models.Account;

/// <summary>
/// Request to revoke a refresh token during logout.
/// </summary>
public sealed class LogoutRequest
{
    /// <summary>
    /// Optional refresh token to revoke. Access tokens naturally expire.
    /// </summary>
    public string? RefreshToken { get; set; }
}
