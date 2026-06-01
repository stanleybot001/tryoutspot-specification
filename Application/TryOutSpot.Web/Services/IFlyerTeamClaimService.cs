using TryOutSpot.Web.Data.Entities;

namespace TryOutSpot.Web.Services;

public interface IFlyerTeamClaimService
{
    Task<IReadOnlyCollection<FlyerTeamClaimCandidate>> GetClaimableTeamsAsync(
        User user,
        CancellationToken cancellationToken);

    Task<FlyerTeamClaimResult> ClaimAsync(
        User user,
        Guid teamId,
        CancellationToken cancellationToken);

    Task<FlyerTeamClaimResult> DismissAsync(
        User user,
        Guid teamId,
        CancellationToken cancellationToken);
}

public sealed record FlyerTeamClaimCandidate(
    Guid TeamId,
    string TeamName,
    string? TeamLevel,
    string? SportName,
    string? City,
    string? State,
    string? ZipCode,
    string MatchedBy,
    int ListingCount,
    DateTime LatestFlyerAt);

public sealed record FlyerTeamClaimResult(
    bool Succeeded,
    string Message,
    bool RefreshSignIn = false);
