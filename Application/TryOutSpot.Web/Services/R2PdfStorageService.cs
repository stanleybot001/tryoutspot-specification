using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace TryOutSpot.Web.Services;

public sealed class R2PdfStorageService(
    IOptions<R2StorageOptions> options,
    ILogger<R2PdfStorageService> logger) : IPdfStorageService
{
    private readonly R2StorageOptions storageOptions = options.Value;

    public async Task UploadPdfAsync(string objectKey, byte[] content, CancellationToken cancellationToken)
    {
        await UploadFileAsync(objectKey, content, "application/pdf", cancellationToken);
    }

    public async Task UploadFileAsync(
        string objectKey,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new ArgumentException("Object key is required.", nameof(objectKey));
        }

        using var client = CreateClient();
        using var stream = new MemoryStream(content, writable: false);
        var request = new PutObjectRequest
        {
            BucketName = storageOptions.BucketName,
            Key = objectKey,
            InputStream = stream,
            ContentType = contentType,
            AutoCloseStream = true,
            UseChunkEncoding = false,
            DisablePayloadSigning = true
        };

        try
        {
            await client.PutObjectAsync(request, cancellationToken);
        }
        catch (AmazonS3Exception exception)
        {
            logger.LogError(
                exception,
                "R2 upload failed. Bucket={BucketName} Endpoint={Endpoint} Key={ObjectKey} StatusCode={StatusCode} ErrorCode={ErrorCode} RequestId={RequestId}",
                storageOptions.BucketName,
                storageOptions.Endpoint,
                objectKey,
                exception.StatusCode,
                exception.ErrorCode,
                exception.RequestId);
            throw;
        }
    }

    public async Task<byte[]?> DownloadPdfAsync(string objectKey, CancellationToken cancellationToken)
    {
        var payload = await DownloadFileAsync(objectKey, cancellationToken);
        return payload?.Content;
    }

    public async Task<StoredObjectPayload?> DownloadFileAsync(string objectKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return null;
        }

        try
        {
            using var client = CreateClient();
            var response = await client.GetObjectAsync(
                new GetObjectRequest
                {
                    BucketName = storageOptions.BucketName,
                    Key = objectKey
                },
                cancellationToken);

            await using var responseStream = response.ResponseStream;
            using var memoryStream = new MemoryStream();
            await responseStream.CopyToAsync(memoryStream, cancellationToken);
            var contentType = string.IsNullOrWhiteSpace(response.Headers.ContentType)
                ? "application/octet-stream"
                : response.Headers.ContentType;
            return new StoredObjectPayload(memoryStream.ToArray(), contentType);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (AmazonS3Exception exception)
        {
            logger.LogError(
                exception,
                "R2 download failed. Bucket={BucketName} Endpoint={Endpoint} Key={ObjectKey} StatusCode={StatusCode} ErrorCode={ErrorCode} RequestId={RequestId}",
                storageOptions.BucketName,
                storageOptions.Endpoint,
                objectKey,
                exception.StatusCode,
                exception.ErrorCode,
                exception.RequestId);
            throw;
        }
    }

    public async Task DeletePdfAsync(string objectKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            return;
        }

        try
        {
            using var client = CreateClient();
            await client.DeleteObjectAsync(
                new DeleteObjectRequest
                {
                    BucketName = storageOptions.BucketName,
                    Key = objectKey
                },
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to delete R2 object {ObjectKey}.", objectKey);
        }
    }

    private AmazonS3Client CreateClient()
    {
        var config = new AmazonS3Config
        {
            ServiceURL = storageOptions.Endpoint,
            ForcePathStyle = true,
            AuthenticationRegion = "auto"
        };

        return new AmazonS3Client(
            storageOptions.AccessKeyId,
            storageOptions.SecretAccessKey,
            config);
    }
}
