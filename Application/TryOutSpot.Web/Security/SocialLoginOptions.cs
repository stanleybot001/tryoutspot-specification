namespace TryOutSpot.Web.Security;

public sealed class SocialLoginOptions
{
    public const string SectionName = "SocialLogin";

    public int ExternalLoginTokenMinutes { get; set; } = 10;
}
