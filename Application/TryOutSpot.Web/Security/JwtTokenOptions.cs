namespace TryOutSpot.Web.Security;

public sealed class JwtTokenOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "TryOutSpot";

    public string Audience { get; set; } = "TryOutSpot.Api";

    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 30;
}
