namespace TryOutSpot.Web.Services;

public sealed class FlyerLocationEnrichmentOptions
{
    public const string SectionName = "FlyerLocationEnrichment";

    public string? ApiKey { get; set; }

    public bool IsEnabled { get; set; } = true;

    public bool EnablePlaceSearch { get; set; } = true;

    public bool EnableReverseLookup { get; set; } = true;

    public bool EnableCityZipFallback { get; set; } = true;

    public string ForwardGeocodeEndpoint { get; set; } = "https://api.latlng.work/api";

    public string ReverseGeocodeEndpoint { get; set; } = "https://api.latlng.work/reverse";

    public string UserAgent { get; set; } = "TryOutSpot/1.0 (https://tryoutspot.com)";

    public string Language { get; set; } = "en";

    public int SearchLimit { get; set; } = 1;
}
