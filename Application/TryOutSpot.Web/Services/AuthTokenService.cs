using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Models.Account;
using TryOutSpot.Web.Security;

namespace TryOutSpot.Web.Services;

public sealed class AuthTokenService(
    AppDbContext dbContext,
    UserManager<User> userManager,
    IOptions<JwtTokenOptions> jwtOptions) : IAuthTokenService
{
    private const string RefreshTokenLoginProvider = "TryOutSpot.RefreshToken";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AuthTokenResponse> CreateTokenResponseAsync(User user, CancellationToken cancellationToken)
    {
        var roles = await userManager.GetRolesAsync(user);
        var now = DateTime.UtcNow;
        var accessTokenExpiresAt = now.AddMinutes(Math.Max(1, jwtOptions.Value.AccessTokenMinutes));
        var refreshTokenExpiresAt = now.AddDays(Math.Max(1, jwtOptions.Value.RefreshTokenDays));
        var roleArray = roles.ToArray();
        var accessToken = CreateAccessToken(user, roleArray, now, accessTokenExpiresAt);
        var refreshToken = await CreateRefreshTokenAsync(user, now, refreshTokenExpiresAt, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return new AuthTokenResponse(
            "Bearer",
            accessToken,
            accessTokenExpiresAt,
            refreshToken,
            refreshTokenExpiresAt,
            ToResponse(user, roleArray));
    }

    public async Task<AuthTokenResponse?> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var parsedToken = ParseRefreshToken(refreshToken);
        if (parsedToken is null)
        {
            return null;
        }

        var userToken = await dbContext.Set<IdentityUserToken<Guid>>()
            .SingleOrDefaultAsync(
                token => token.LoginProvider == RefreshTokenLoginProvider
                    && token.Name == parsedToken.Value.TokenId,
                cancellationToken);

        if (userToken is null || !TryReadRefreshToken(userToken.Value, out var storedToken))
        {
            return null;
        }

        if (storedToken.RevokedAtUtc is not null || storedToken.ExpiresAtUtc <= DateTime.UtcNow)
        {
            dbContext.Set<IdentityUserToken<Guid>>().Remove(userToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return null;
        }

        if (!IsTokenHashMatch(parsedToken.Value.Secret, storedToken.TokenHash))
        {
            return null;
        }

        var user = await userManager.FindByIdAsync(userToken.UserId.ToString());
        if (user is not { IsActive: true })
        {
            return null;
        }

        dbContext.Set<IdentityUserToken<Guid>>().Remove(userToken);
        return await CreateTokenResponseAsync(user, cancellationToken);
    }

    public async Task RevokeRefreshTokenAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        var parsedToken = ParseRefreshToken(refreshToken);
        if (parsedToken is null)
        {
            return;
        }

        var userToken = await dbContext.Set<IdentityUserToken<Guid>>()
            .SingleOrDefaultAsync(
                token => token.LoginProvider == RefreshTokenLoginProvider
                    && token.Name == parsedToken.Value.TokenId,
                cancellationToken);

        if (userToken is null || !TryReadRefreshToken(userToken.Value, out var storedToken))
        {
            return;
        }

        if (!IsTokenHashMatch(parsedToken.Value.Secret, storedToken.TokenHash))
        {
            return;
        }

        userToken.Value = JsonSerializer.Serialize(
            storedToken with { RevokedAtUtc = DateTime.UtcNow },
            JsonOptions);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private string CreateAccessToken(
        User user,
        IReadOnlyCollection<string> roles,
        DateTime issuedAtUtc,
        DateTime expiresAtUtc)
    {
        var signingKey = new SymmetricSecurityKey(GetSigningKeyBytes());
        var signingCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Name, user.Email ?? string.Empty),
            new("security_stamp", user.SecurityStamp ?? string.Empty)
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var token = new JwtSecurityToken(
            issuer: jwtOptions.Value.Issuer,
            audience: jwtOptions.Value.Audience,
            claims: claims,
            notBefore: issuedAtUtc,
            expires: expiresAtUtc,
            signingCredentials: signingCredentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private async Task<string> CreateRefreshTokenAsync(
        User user,
        DateTime createdAtUtc,
        DateTime expiresAtUtc,
        CancellationToken cancellationToken)
    {
        var tokenId = Guid.NewGuid().ToString("N");
        var secret = Base64Url(RandomNumberGenerator.GetBytes(64));
        var tokenHash = HashTokenSecret(secret);
        var storedToken = new StoredRefreshToken(tokenHash, createdAtUtc, expiresAtUtc, null);

        dbContext.Set<IdentityUserToken<Guid>>().Add(new IdentityUserToken<Guid>
        {
            UserId = user.Id,
            LoginProvider = RefreshTokenLoginProvider,
            Name = tokenId,
            Value = JsonSerializer.Serialize(storedToken, JsonOptions)
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return $"{tokenId}.{secret}";
    }

    private static UserAccountResponse ToResponse(User user, IReadOnlyCollection<string> roles)
    {
        return new UserAccountResponse(
            user.Id,
            user.Email ?? string.Empty,
            user.FirstName,
            user.LastName,
            roles,
            user.IsActive);
    }

    private byte[] GetSigningKeyBytes()
    {
        var signingKey = jwtOptions.Value.SigningKey;
        if (string.IsNullOrWhiteSpace(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey must be at least 32 UTF-8 bytes.");
        }

        return Encoding.UTF8.GetBytes(signingKey);
    }

    private static ParsedRefreshToken? ParseRefreshToken(string? refreshToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var parts = refreshToken.Split('.', 2);
        return parts.Length == 2
            && parts[0].Length == 32
            && !string.IsNullOrWhiteSpace(parts[1])
            ? new ParsedRefreshToken(parts[0], parts[1])
            : null;
    }

    private static bool TryReadRefreshToken(string? value, out StoredRefreshToken storedToken)
    {
        try
        {
            storedToken = JsonSerializer.Deserialize<StoredRefreshToken>(value ?? string.Empty, JsonOptions)!;
            return storedToken is not null;
        }
        catch (JsonException)
        {
            storedToken = default!;
            return false;
        }
    }

    private static bool IsTokenHashMatch(string secret, string storedHash)
    {
        var presentedHashBytes = Convert.FromBase64String(HashTokenSecret(secret));
        var storedHashBytes = Convert.FromBase64String(storedHash);
        return CryptographicOperations.FixedTimeEquals(presentedHashBytes, storedHashBytes);
    }

    private static string HashTokenSecret(string secret)
    {
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
    }

    private static string Base64Url(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private readonly record struct ParsedRefreshToken(string TokenId, string Secret);

    private sealed record StoredRefreshToken(
        string TokenHash,
        DateTime CreatedAtUtc,
        DateTime ExpiresAtUtc,
        DateTime? RevokedAtUtc);
}
