namespace TryOutSpot.Web.Services;

public interface IZipRadiusSearchService
{
    string? NormalizeZipCode(string? zipCode);

    int ClampRadiusMiles(int? radiusMiles);

    Task<ZipRadiusSearchResult?> ResolveZipCodesWithinRadiusAsync(
        string originZipCode,
        int radiusMiles,
        CancellationToken cancellationToken);
}

public sealed record ZipDistanceResult(
    string ZipCode,
    double DistanceMiles);

public sealed record ZipRadiusSearchResult(
    string OriginZipCode,
    int RadiusMiles,
    double OriginLatitude,
    double OriginLongitude,
    IReadOnlyCollection<ZipDistanceResult> ZipDistances)
{
    public IReadOnlyCollection<string> ZipCodes => ZipDistances.Select(result => result.ZipCode).ToArray();

    public IReadOnlyDictionary<string, double> DistanceByZipCode =>
        ZipDistances
            .GroupBy(result => result.ZipCode, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().DistanceMiles, StringComparer.Ordinal);
}
