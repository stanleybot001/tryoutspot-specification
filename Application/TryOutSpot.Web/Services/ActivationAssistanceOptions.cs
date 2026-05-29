namespace TryOutSpot.Web.Services;

public sealed class ActivationAssistanceOptions
{
    public const string SectionName = "ActivationAssistance";

    public string SupportEmail { get; set; } = "support@tryoutspot.com";

    public string WhatsAppUrl { get; set; } = string.Empty;

    public int SignupGraceHours { get; set; } = 24;

    public int DismissalCooldownDays { get; set; } = 14;
}
