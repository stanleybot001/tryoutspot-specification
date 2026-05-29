using TryOutSpot.Web.Models.Dashboard;

namespace TryOutSpot.Web.Services;

public interface IActivationAssistanceService
{
    Task<ActivationAssistancePromptResponse> GetTeamFirstListingPromptAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<ActivationAssistanceActionResponse> DismissTeamFirstListingPromptAsync(
        Guid userId,
        string? promptKey,
        Guid? teamId,
        CancellationToken cancellationToken);
}
