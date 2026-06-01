namespace TryOutSpot.Web.Services;

public interface IFlyerImportRemoteFileFetcher
{
    Task<FlyerImportRemoteFileFetchResult> FetchAsync(
        string imageUrl,
        CancellationToken cancellationToken);
}

public sealed record FlyerImportRemoteFileFetchResult(
    bool Succeeded,
    UploadedFlyerImportFile? File,
    IReadOnlyCollection<string> Errors)
{
    public static FlyerImportRemoteFileFetchResult Success(UploadedFlyerImportFile file)
    {
        return new FlyerImportRemoteFileFetchResult(true, file, []);
    }

    public static FlyerImportRemoteFileFetchResult Failure(params string[] errors)
    {
        return new FlyerImportRemoteFileFetchResult(false, null, errors);
    }
}
