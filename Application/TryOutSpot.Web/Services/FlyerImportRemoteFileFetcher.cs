using System.Net;

namespace TryOutSpot.Web.Services;

public sealed class FlyerImportRemoteFileFetcher(
    HttpClient httpClient,
    ILogger<FlyerImportRemoteFileFetcher> logger) : IFlyerImportRemoteFileFetcher
{
    private const int MaxFlyerSizeMegabytes = 10;
    private const long MaxFlyerSizeBytes = MaxFlyerSizeMegabytes * 1024L * 1024L;

    public async Task<FlyerImportRemoteFileFetchResult> FetchAsync(
        string imageUrl,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || IsBlockedHost(uri))
        {
            return FlyerImportRemoteFileFetchResult.Failure("Add a valid public image URL.");
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return FlyerImportRemoteFileFetchResult.Failure("The image URL could not be downloaded.");
            }

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength is > MaxFlyerSizeBytes)
            {
                return FlyerImportRemoteFileFetchResult.Failure($"Flyer files can be up to {MaxFlyerSizeMegabytes} MB.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var memoryStream = new MemoryStream();
            var buffer = new byte[81920];
            long totalBytes = 0;
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                totalBytes += read;
                if (totalBytes > MaxFlyerSizeBytes)
                {
                    return FlyerImportRemoteFileFetchResult.Failure($"Flyer files can be up to {MaxFlyerSizeMegabytes} MB.");
                }

                memoryStream.Write(buffer, 0, read);
            }

            var contentType = response.Content.Headers.ContentType?.MediaType
                ?? ResolveContentTypeFromPath(uri.AbsolutePath)
                ?? "application/octet-stream";
            var fileName = Path.GetFileName(uri.AbsolutePath);
            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = "facebook-flyer";
            }

            var parsed = FlyerImportUploadHelper.ParsePayload(
                fileName,
                memoryStream.ToArray(),
                contentType);
            return parsed.Succeeded && parsed.File is not null
                ? FlyerImportRemoteFileFetchResult.Success(parsed.File)
                : FlyerImportRemoteFileFetchResult.Failure(parsed.Errors.ToArray());
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to download flyer image URL {ImageUrl}.", imageUrl);
            return FlyerImportRemoteFileFetchResult.Failure("The image URL could not be downloaded.");
        }
    }

    private static string? ResolveContentTypeFromPath(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            _ => null
        };
    }

    private static bool IsBlockedHost(Uri uri)
    {
        if (uri.IsLoopback)
        {
            return true;
        }

        return IPAddress.TryParse(uri.Host, out var address) && IsPrivateOrSpecialAddress(address);
    }

    private static bool IsPrivateOrSpecialAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)
            || address.IsIPv6LinkLocal
            || address.IsIPv6Multicast
            || address.IsIPv6SiteLocal)
        {
            return true;
        }

        if (address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] switch
        {
            10 => true,
            127 => true,
            169 when bytes[1] == 254 => true,
            172 when bytes[1] is >= 16 and <= 31 => true,
            192 when bytes[1] == 168 => true,
            _ => false
        };
    }
}
