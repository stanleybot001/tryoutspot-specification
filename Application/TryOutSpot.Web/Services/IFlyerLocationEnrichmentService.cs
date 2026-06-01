namespace TryOutSpot.Web.Services;

public interface IFlyerLocationEnrichmentService
{
    Task<FlyerImportCreateInput> EnrichAsync(
        FlyerImportCreateInput input,
        CancellationToken cancellationToken);
}
