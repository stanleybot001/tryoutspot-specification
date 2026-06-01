namespace TryOutSpot.Web.Models.Dashboard;

public static class ActivationAssistancePromptKeys
{
    public const string TeamFirstListing = "team_first_listing";
    public const string FlyerTeamClaim = "flyer_team_claim";
}

public sealed record ActivationAssistancePromptResponse(
    bool ShouldShow,
    string PromptKey,
    string Title,
    string Message,
    Guid? TeamId,
    string? TeamName,
    IReadOnlyCollection<ActivationAssistanceReasonResponse> Reasons,
    string? TeamProfileUrl,
    string? CreateListingUrl,
    string? SupportEmail,
    string? SupportEmailUrl,
    string? WhatsAppUrl);

public sealed record ActivationAssistanceReasonResponse(
    string Code,
    string Label);

public sealed class DismissActivationAssistanceRequest
{
    public string? PromptKey { get; init; }

    public Guid? TeamId { get; init; }
}

public sealed record ActivationAssistanceActionResponse(DateTime RecordedAt);
