using Microsoft.EntityFrameworkCore;
using TryOutSpot.Web.Data;
using TryOutSpot.Web.Data.Entities;

namespace TryOutSpot.Web.Services;

public sealed class ZipRadiusSearchService(AppDbContext dbContext) : IZipRadiusSearchService
{
    public const int MinRadiusMiles = 1;
    public const int DefaultRadiusMiles = 25;
    public const int MaxRadiusMiles = 800;

    public string? NormalizeZipCode(string? zipCode)
    {
        if (string.IsNullOrWhiteSpace(zipCode))
        {
            return null;
        }

        var digits = new string(zipCode.Trim().Where(char.IsDigit).ToArray());
        if (digits.Length < 5)
        {
            return null;
        }

        return digits[..5];
    }

    public int ClampRadiusMiles(int? radiusMiles)
    {
        if (!radiusMiles.HasValue)
        {
            return DefaultRadiusMiles;
        }

        return Math.Clamp(radiusMiles.Value, MinRadiusMiles, MaxRadiusMiles);
    }

    public async Task<ZipRadiusSearchResult?> ResolveZipCodesWithinRadiusAsync(
        string originZipCode,
        int radiusMiles,
        CancellationToken cancellationToken)
    {
        var normalizedOriginZip = NormalizeZipCode(originZipCode);
        if (normalizedOriginZip is null)
        {
            return null;
        }

        radiusMiles = ClampRadiusMiles(radiusMiles);

        var origin = await dbContext.ZipCodeGeographies
            .AsNoTracking()
            .Where(zip => zip.IsActive)
            .SingleOrDefaultAsync(zip => zip.ZipCode == normalizedOriginZip, cancellationToken);

        if (origin is null)
        {
            return null;
        }

        var originLatitude = (double)origin.Latitude;
        var originLongitude = (double)origin.Longitude;

        // Bounding box first to keep the candidate query set small.
        var latitudeDelta = radiusMiles / 69d;
        var cosLatitude = Math.Cos(ToRadians(originLatitude));
        var safeCosLatitude = Math.Abs(cosLatitude) < 0.01d ? 0.01d : cosLatitude;
        var longitudeDelta = radiusMiles / (69.172d * Math.Abs(safeCosLatitude));

        var minLatitude = originLatitude - latitudeDelta;
        var maxLatitude = originLatitude + latitudeDelta;
        var minLongitude = originLongitude - longitudeDelta;
        var maxLongitude = originLongitude + longitudeDelta;

        var candidates = await dbContext.ZipCodeGeographies
            .AsNoTracking()
            .Where(zip => zip.IsActive)
            .Where(zip => zip.Latitude >= (decimal)minLatitude && zip.Latitude <= (decimal)maxLatitude)
            .Where(zip => zip.Longitude >= (decimal)minLongitude && zip.Longitude <= (decimal)maxLongitude)
            .Select(zip => new
            {
                zip.ZipCode,
                Latitude = (double)zip.Latitude,
                Longitude = (double)zip.Longitude
            })
            .ToArrayAsync(cancellationToken);

        var matches = candidates
            .Select(candidate => new ZipDistanceResult(
                candidate.ZipCode,
                HaversineMiles(
                    originLatitude,
                    originLongitude,
                    candidate.Latitude,
                    candidate.Longitude)))
            .Where(distance => distance.DistanceMiles <= radiusMiles)
            .OrderBy(distance => distance.DistanceMiles)
            .ToArray();

        return new ZipRadiusSearchResult(
            normalizedOriginZip,
            radiusMiles,
            originLatitude,
            originLongitude,
            matches);
    }

    private static double HaversineMiles(
        double latitudeA,
        double longitudeA,
        double latitudeB,
        double longitudeB)
    {
        const double earthRadiusMiles = 3958.756d;

        var latitudeDelta = ToRadians(latitudeB - latitudeA);
        var longitudeDelta = ToRadians(longitudeB - longitudeA);
        var a = Math.Pow(Math.Sin(latitudeDelta / 2d), 2d)
            + Math.Cos(ToRadians(latitudeA))
            * Math.Cos(ToRadians(latitudeB))
            * Math.Pow(Math.Sin(longitudeDelta / 2d), 2d);

        var c = 2d * Math.Asin(Math.Min(1d, Math.Sqrt(a)));
        return earthRadiusMiles * c;
    }

    private static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180d;
    }
}
