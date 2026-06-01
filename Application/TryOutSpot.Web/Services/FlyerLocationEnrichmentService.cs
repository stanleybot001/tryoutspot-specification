using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TryOutSpot.Web.Data;

namespace TryOutSpot.Web.Services;

public sealed class FlyerLocationEnrichmentService(
    AppDbContext dbContext,
    IFlyerPlaceSearchClient placeSearchClient,
    IOptions<FlyerLocationEnrichmentOptions> options,
    ILogger<FlyerLocationEnrichmentService> logger) : IFlyerLocationEnrichmentService
{
    public async Task<FlyerImportCreateInput> EnrichAsync(
        FlyerImportCreateInput input,
        CancellationToken cancellationToken)
    {
        var enrichmentOptions = options.Value;
        if (!enrichmentOptions.IsEnabled)
        {
            return input;
        }

        var enrichedInput = input with
        {
            State = FlyerLocationNormalization.NormalizeState(input.State),
            ZipCode = FlyerLocationNormalization.NormalizeZipCode(input.ZipCode)
        };
        if (!string.IsNullOrWhiteSpace(enrichedInput.ZipCode))
        {
            return enrichedInput;
        }

        if (enrichmentOptions.EnablePlaceSearch)
        {
            enrichedInput = await TryEnrichFromPlaceSearchAsync(enrichedInput, cancellationToken);
        }

        if (enrichmentOptions.EnableCityZipFallback
            && string.IsNullOrWhiteSpace(enrichedInput.ZipCode))
        {
            enrichedInput = await TryEnrichFromCityZipAsync(enrichedInput, cancellationToken);
        }

        return enrichedInput;
    }

    private async Task<FlyerImportCreateInput> TryEnrichFromPlaceSearchAsync(
        FlyerImportCreateInput input,
        CancellationToken cancellationToken)
    {
        var query = BuildPlaceSearchQuery(input);
        if (query is null)
        {
            return input;
        }

        try
        {
            var result = await placeSearchClient.SearchAsync(query, cancellationToken);
            if (result is null)
            {
                return input;
            }

            return input with
            {
                Location = FlyerLocationNormalization.NormalizeLength(input.Location, 500)
                    ?? FlyerLocationNormalization.NormalizeLength(result.Name, 500),
                Address = FlyerLocationNormalization.NormalizeLength(input.Address, 500)
                    ?? FlyerLocationNormalization.NormalizeLength(result.Address, 500),
                City = FlyerLocationNormalization.NormalizeLength(input.City, 100)
                    ?? FlyerLocationNormalization.NormalizeLength(result.City, 100),
                State = FlyerLocationNormalization.NormalizeState(input.State)
                    ?? FlyerLocationNormalization.NormalizeState(result.State),
                ZipCode = FlyerLocationNormalization.NormalizeZipCode(input.ZipCode)
                    ?? FlyerLocationNormalization.NormalizeZipCode(result.ZipCode)
            };
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Flyer location place lookup failed for query {Query}.", query);
            return input;
        }
    }

    private async Task<FlyerImportCreateInput> TryEnrichFromCityZipAsync(
        FlyerImportCreateInput input,
        CancellationToken cancellationToken)
    {
        var cityKey = FlyerLocationNormalization.NormalizeCityKey(input.City);
        var state = FlyerLocationNormalization.NormalizeState(input.State);
        if (cityKey is null || state is null)
        {
            return input;
        }

        var match = await dbContext.ZipCodeGeographies
            .AsNoTracking()
            .Where(zip => zip.IsActive && zip.State == state && zip.City != null)
            .Select(zip => new
            {
                zip.ZipCode,
                zip.City,
                zip.State,
                CityKey = zip.City!.ToLower()
                    .Replace("'", string.Empty)
                    .Replace("’", string.Empty)
                    .Replace(".", string.Empty)
                    .Replace("-", string.Empty)
                    .Replace(" ", string.Empty)
            })
            .Where(zip => zip.CityKey == cityKey)
            .OrderBy(zip => zip.ZipCode)
            .FirstOrDefaultAsync(cancellationToken);

        return match is null
            ? input
            : input with
            {
                City = FlyerLocationNormalization.NormalizeLength(input.City, 100)
                    ?? FlyerLocationNormalization.NormalizeLength(match.City, 100),
                State = state,
                ZipCode = FlyerLocationNormalization.NormalizeZipCode(match.ZipCode)
            };
    }

    private static string? BuildPlaceSearchQuery(FlyerImportCreateInput input)
    {
        var location = FlyerLocationNormalization.NormalizeOptional(input.Location);
        var address = FlyerLocationNormalization.NormalizeOptional(input.Address);
        if (location is null && address is null)
        {
            return null;
        }

        var parts = new List<string>();
        if (location is not null)
        {
            parts.Add(location);
        }

        if (address is not null
            && !string.Equals(address, location, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add(address);
        }

        var city = FlyerLocationNormalization.NormalizeOptional(input.City);
        var state = FlyerLocationNormalization.NormalizeState(input.State)
            ?? FlyerLocationNormalization.NormalizeOptional(input.State);
        if (city is not null && state is not null)
        {
            parts.Add($"{city}, {state}");
        }
        else if (city is not null)
        {
            parts.Add(city);
        }
        else if (state is not null)
        {
            parts.Add(state);
        }

        parts.Add("United States");
        return string.Join(", ", parts);
    }
}
