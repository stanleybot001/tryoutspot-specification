namespace TryOutSpot.Web.Security;

public sealed class GoogleAuthenticationOptions
{
    public const string SectionName = "Authentication:Google";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public string CallbackPath { get; set; } = "/signin-google";

    public bool IsConfigured =>
        HasConfiguredValue(ClientId)
        && HasConfiguredValue(ClientSecret);

    private static bool HasConfiguredValue(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && !value.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("PUT_", StringComparison.OrdinalIgnoreCase);
    }
}
