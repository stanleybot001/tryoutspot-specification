using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace TryOutSpot.Web.Services;

public sealed class LatLngFlyerPlaceSearchClient(
    HttpClient httpClient,
    IOptions<FlyerLocationEnrichmentOptions> options,
    ILogger<LatLngFlyerPlaceSearchClient> logger) : IFlyerPlaceSearchClient
{
    public async Task<FlyerPlaceSearchResult?> SearchAsync(
        string query,
        CancellationToken cancellationToken)
    {
        var enrichmentOptions = options.Value;
        var apiKey = FlyerLocationNormalization.NormalizeOptional(enrichmentOptions.ApiKey);
        if (apiKey is null
            || string.IsNullOrWhiteSpace(enrichmentOptions.ForwardGeocodeEndpoint))
        {
            return null;
        }

        var feature = await ForwardGeocodeAsync(query, enrichmentOptions, apiKey, cancellationToken);
        if (feature is null)
        {
            return null;
        }

        var place = ToPlaceSearchResult(feature);
        if (enrichmentOptions.EnableReverseLookup
            && NeedsReverseLookup(place)
            && TryGetCoordinates(feature, out var latitude, out var longitude))
        {
            var reverseFeature = await ReverseGeocodeAsync(
                latitude,
                longitude,
                enrichmentOptions,
                apiKey,
                cancellationToken);
            if (reverseFeature is not null)
            {
                place = Merge(place, ToPlaceSearchResult(reverseFeature));
            }
        }

        return place;
    }

    private async Task<LatLngFeature?> ForwardGeocodeAsync(
        string query,
        FlyerLocationEnrichmentOptions enrichmentOptions,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var requestUrl = QueryHelpers.AddQueryString(
            enrichmentOptions.ForwardGeocodeEndpoint,
            new Dictionary<string, string?>
            {
                ["q"] = query,
                ["limit"] = Math.Clamp(enrichmentOptions.SearchLimit, 1, 5).ToString(CultureInfo.InvariantCulture),
                ["lang"] = FlyerLocationNormalization.NormalizeOptional(enrichmentOptions.Language)
            });

        return await SendFeatureRequestAsync(requestUrl, apiKey, enrichmentOptions.UserAgent, cancellationToken);
    }

    private async Task<LatLngFeature?> ReverseGeocodeAsync(
        double latitude,
        double longitude,
        FlyerLocationEnrichmentOptions enrichmentOptions,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(enrichmentOptions.ReverseGeocodeEndpoint))
        {
            return null;
        }

        var requestUrl = QueryHelpers.AddQueryString(
            enrichmentOptions.ReverseGeocodeEndpoint,
            new Dictionary<string, string?>
            {
                ["lat"] = latitude.ToString(CultureInfo.InvariantCulture),
                ["lon"] = longitude.ToString(CultureInfo.InvariantCulture)
            });

        return await SendFeatureRequestAsync(requestUrl, apiKey, enrichmentOptions.UserAgent, cancellationToken);
    }

    private async Task<LatLngFeature?> SendFeatureRequestAsync(
        string requestUrl,
        string apiKey,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
        request.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            FlyerLocationNormalization.NormalizeOptional(userAgent) ?? "TryOutSpot/1.0");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "LatLng flyer location lookup failed. StatusCode={StatusCode}",
                (int)response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadFromJsonAsync<LatLngFeatureCollection>(cancellationToken);
        return json?.Features?.FirstOrDefault();
    }

    private static FlyerPlaceSearchResult ToPlaceSearchResult(LatLngFeature feature)
    {
        var properties = feature.Properties;
        var name = properties?.GetString("name")
            ?? properties?.GetString("label")
            ?? properties?.GetString("display_name")
            ?? properties?.GetString("formatted");
        var city = properties?.GetString("city")
            ?? properties?.GetString("town")
            ?? properties?.GetString("village")
            ?? properties?.GetString("municipality")
            ?? properties?.GetString("locality")
            ?? properties?.GetString("county");
        var state = FlyerLocationNormalization.NormalizeState(
            properties?.GetString("statecode")
            ?? properties?.GetString("state_code")
            ?? properties?.GetString("state"));
        var zipCode = properties?.GetString("postcode")
            ?? properties?.GetString("postalcode")
            ?? properties?.GetString("postal_code")
            ?? properties?.GetString("zip");
        var address = BuildStreetAddress(properties);

        return new FlyerPlaceSearchResult(
            name,
            address,
            city,
            state,
            zipCode);
    }

    private static string? BuildStreetAddress(LatLngProperties? properties)
    {
        if (properties is null)
        {
            return null;
        }

        var street = properties.GetString("street")
            ?? properties.GetString("road")
            ?? properties.GetString("pedestrian")
            ?? properties.GetString("footway")
            ?? properties.GetString("path");
        if (string.IsNullOrWhiteSpace(street))
        {
            return properties.GetString("address");
        }

        var houseNumber = properties.GetString("housenumber")
            ?? properties.GetString("house_number");

        return string.IsNullOrWhiteSpace(houseNumber) ? street : $"{houseNumber} {street}";
    }

    private static bool NeedsReverseLookup(FlyerPlaceSearchResult place)
    {
        return string.IsNullOrWhiteSpace(place.ZipCode)
            || string.IsNullOrWhiteSpace(place.City)
            || string.IsNullOrWhiteSpace(place.State)
            || string.IsNullOrWhiteSpace(place.Address);
    }

    private static FlyerPlaceSearchResult Merge(
        FlyerPlaceSearchResult primary,
        FlyerPlaceSearchResult fallback)
    {
        return primary with
        {
            Name = FlyerLocationNormalization.NormalizeOptional(primary.Name)
                ?? FlyerLocationNormalization.NormalizeOptional(fallback.Name),
            Address = FlyerLocationNormalization.NormalizeOptional(primary.Address)
                ?? FlyerLocationNormalization.NormalizeOptional(fallback.Address),
            City = FlyerLocationNormalization.NormalizeOptional(primary.City)
                ?? FlyerLocationNormalization.NormalizeOptional(fallback.City),
            State = FlyerLocationNormalization.NormalizeState(primary.State)
                ?? FlyerLocationNormalization.NormalizeState(fallback.State),
            ZipCode = FlyerLocationNormalization.NormalizeZipCode(primary.ZipCode)
                ?? FlyerLocationNormalization.NormalizeZipCode(fallback.ZipCode)
        };
    }

    private static bool TryGetCoordinates(
        LatLngFeature feature,
        out double latitude,
        out double longitude)
    {
        latitude = 0;
        longitude = 0;
        var coordinates = feature.Geometry?.Coordinates;
        if (coordinates is null || coordinates.Length < 2)
        {
            return false;
        }

        longitude = coordinates[0];
        latitude = coordinates[1];
        return true;
    }

    private sealed class LatLngFeatureCollection
    {
        [JsonPropertyName("features")]
        public LatLngFeature[]? Features { get; set; }
    }

    private sealed class LatLngFeature
    {
        [JsonPropertyName("geometry")]
        public LatLngGeometry? Geometry { get; set; }

        [JsonPropertyName("properties")]
        public LatLngProperties? Properties { get; set; }
    }

    private sealed class LatLngGeometry
    {
        [JsonPropertyName("coordinates")]
        public double[]? Coordinates { get; set; }
    }

    private sealed class LatLngProperties
    {
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Values { get; set; }

        public string? GetString(string key)
        {
            if (Values is null || !Values.TryGetValue(key, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => FlyerLocationNormalization.NormalizeOptional(value.GetString()),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            };
        }
    }
}
