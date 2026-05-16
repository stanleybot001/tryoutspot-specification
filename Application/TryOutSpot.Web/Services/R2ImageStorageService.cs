using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace TryOutSpot.Web.Services;

public sealed class R2ImageStorageService(
    IOptions<R2StorageOptions> options,
    ILogger<R2ImageStorageService> logger) : IImageStorageService
{
    private readonly R2StorageOptions storageOptions = options.Value;

    public async Task UploadImageAsync(
        string objectKey,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            throw new ArgumentException("Object key is required.", nameof(objectKey));
        }

        if (content.Length == 0)
        {
            throw new ArgumentException("Content is required.", nameof(content));
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

        await client.PutObjectAsync(request, cancellationToken);
    }

    public async Task<StoredObjectPayload?> DownloadImageAsync(string objectKey, CancellationToken cancellationToken)
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
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to download R2 image object {ObjectKey}.", objectKey);
            return null;
        }
    }

    public async Task DeleteImageAsync(string objectKey, CancellationToken cancellationToken)
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
            logger.LogWarning(exception, "Failed to delete R2 image object {ObjectKey}.", objectKey);
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
