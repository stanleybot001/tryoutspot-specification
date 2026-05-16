namespace TryOutSpot.Web.Services;

public interface IPdfStorageService
{
    Task UploadPdfAsync(string objectKey, byte[] content, CancellationToken cancellationToken);

    Task<byte[]?> DownloadPdfAsync(string objectKey, CancellationToken cancellationToken);

    Task DeletePdfAsync(string objectKey, CancellationToken cancellationToken);
}
