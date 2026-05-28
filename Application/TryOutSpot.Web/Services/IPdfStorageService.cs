namespace TryOutSpot.Web.Services;

public interface IPdfStorageService
{
    Task UploadPdfAsync(string objectKey, byte[] content, CancellationToken cancellationToken);

    Task UploadFileAsync(string objectKey, byte[] content, string contentType, CancellationToken cancellationToken);

    Task<byte[]?> DownloadPdfAsync(string objectKey, CancellationToken cancellationToken);

    Task<StoredObjectPayload?> DownloadFileAsync(string objectKey, CancellationToken cancellationToken);

    Task DeletePdfAsync(string objectKey, CancellationToken cancellationToken);
}
