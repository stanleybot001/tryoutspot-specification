namespace TryOutSpot.Web.Services;

public interface IImageStorageService
{
    Task UploadImageAsync(
        string objectKey,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken);

    Task<StoredObjectPayload?> DownloadImageAsync(string objectKey, CancellationToken cancellationToken);

    Task DeleteImageAsync(string objectKey, CancellationToken cancellationToken);
}

public sealed record StoredObjectPayload(byte[] Content, string ContentType);
