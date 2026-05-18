using TryOutSpot.Web.Models.Dashboard;

namespace TryOutSpot.Web.Services;

public interface IDashboardActivityService
{
    Task<DashboardActivityPreferencesResponse> GetPreferencesAsync(
        Guid userId,
        CancellationToken cancellationToken);

    Task<DashboardActivityPreferencesResponse> UpdatePreferencesAsync(
        Guid userId,
        IReadOnlyCollection<string> activityTypes,
        CancellationToken cancellationToken);

    Task<DashboardRecentActivityResponse> GetRecentActivityAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<DashboardActivityViewedResponse> MarkViewedAsync(
        Guid userId,
        CancellationToken cancellationToken);
}
