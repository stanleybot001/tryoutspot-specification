namespace TryOutSpot.Web.Services;

public interface IFlyerPlaceSearchClient
{
    Task<FlyerPlaceSearchResult?> SearchAsync(
        string query,
        CancellationToken cancellationToken);
}

public sealed record FlyerPlaceSearchResult(
    string? Name,
    string? Address,
    string? City,
    string? State,
    string? ZipCode);
