using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Models.Account;

namespace TryOutSpot.Web.Services;

public interface IAuthTokenService
{
    Task<AuthTokenResponse> CreateTokenResponseAsync(User user, CancellationToken cancellationToken);

    Task<AuthTokenResponse?> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken);

    Task RevokeRefreshTokenAsync(string? refreshToken, CancellationToken cancellationToken);
}
