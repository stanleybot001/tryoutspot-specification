using Microsoft.AspNetCore.Http;

namespace TryOutSpot.Web.Services;

public static class FlyerImportUploadHelper
{
    private const int MaxFlyerSizeMegabytes = 10;
    private const long MaxFlyerSizeBytes = MaxFlyerSizeMegabytes * 1024L * 1024L;

    private static readonly string[] SupportedContentTypes =
    [
        "application/pdf",
        "image/jpeg",
        "image/png",
        "image/webp"
    ];

    public static async Task<FlyerImportUploadParseResult> ParseAsync(
        IFormFile? uploadedFlyer,
        CancellationToken cancellationToken)
    {
        if (uploadedFlyer is null || uploadedFlyer.Length == 0)
        {
            return FlyerImportUploadParseResult.NoFile();
        }

        if (uploadedFlyer.Length > MaxFlyerSizeBytes)
        {
            return FlyerImportUploadParseResult.Failure($"Flyer files can be up to {MaxFlyerSizeMegabytes} MB.");
        }

        var contentType = ResolveContentType(uploadedFlyer.ContentType, uploadedFlyer.FileName);
        if (contentType is null)
        {
            return FlyerImportUploadParseResult.Failure("Only PDF, JPG, PNG, or WEBP flyer files are supported.");
        }

        await using var stream = uploadedFlyer.OpenReadStream();
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);

        return ParsePayload(
            uploadedFlyer.FileName,
            memoryStream.ToArray(),
            contentType);
    }

    public static FlyerImportUploadParseResult ParsePayload(
        string fileName,
        byte[] content,
        string contentType)
    {
        var resolvedContentType = ResolveContentType(contentType, fileName);
        if (resolvedContentType is null)
        {
            return FlyerImportUploadParseResult.Failure("Only PDF, JPG, PNG, or WEBP flyer files are supported.");
        }

        if (content.Length <= 0)
        {
            return FlyerImportUploadParseResult.Failure("Upload a PDF or image flyer.");
        }

        if (content.Length > MaxFlyerSizeBytes)
        {
            return FlyerImportUploadParseResult.Failure($"Flyer files can be up to {MaxFlyerSizeMegabytes} MB.");
        }

        if (string.Equals(resolvedContentType, "application/pdf", StringComparison.Ordinal) && !LooksLikePdf(content))
        {
            return FlyerImportUploadParseResult.Failure("Uploaded file is not a valid PDF.");
        }

        if (resolvedContentType.StartsWith("image/", StringComparison.Ordinal)
            && !LooksLikeSupportedImage(content, resolvedContentType))
        {
            return FlyerImportUploadParseResult.Failure("Uploaded file is not a valid JPG, PNG, or WEBP image.");
        }

        return FlyerImportUploadParseResult.Success(new UploadedFlyerImportFile(
            BuildSafeFileName(fileName, resolvedContentType),
            content,
            resolvedContentType));
    }

    private static bool LooksLikePdf(byte[] content)
    {
        return content.Length >= 5
            && content[0] == 0x25
            && content[1] == 0x50
            && content[2] == 0x44
            && content[3] == 0x46
            && content[4] == 0x2D;
    }

    private static bool LooksLikeSupportedImage(byte[] content, string contentType)
    {
        return contentType switch
        {
            "image/jpeg" => content.Length >= 3
                && content[0] == 0xFF
                && content[1] == 0xD8
                && content[2] == 0xFF,
            "image/png" => content.Length >= 8
                && content[0] == 0x89
                && content[1] == 0x50
                && content[2] == 0x4E
                && content[3] == 0x47
                && content[4] == 0x0D
                && content[5] == 0x0A
                && content[6] == 0x1A
                && content[7] == 0x0A,
            "image/webp" => content.Length >= 12
                && content[0] == 0x52
                && content[1] == 0x49
                && content[2] == 0x46
                && content[3] == 0x46
                && content[8] == 0x57
                && content[9] == 0x45
                && content[10] == 0x42
                && content[11] == 0x50,
            _ => false
        };
    }

    private static string BuildSafeFileName(string originalFileName, string contentType)
    {
        var baseName = Path.GetFileNameWithoutExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "flyer";
        }

        var invalidFileNameChars = Path.GetInvalidFileNameChars();
        var safeBaseName = new string(
            baseName
                .Trim()
                .Select(character => invalidFileNameChars.Contains(character) ? '-' : character)
                .ToArray());
        if (string.IsNullOrWhiteSpace(safeBaseName))
        {
            safeBaseName = "flyer";
        }

        var extension = contentType switch
        {
            "application/pdf" => ".pdf",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => Path.GetExtension(originalFileName)
        };
        var maxBaseNameLength = Math.Max(1, 260 - extension.Length);
        if (safeBaseName.Length > maxBaseNameLength)
        {
            safeBaseName = safeBaseName[..maxBaseNameLength];
        }

        return $"{safeBaseName}{extension}";
    }

    private static string? ResolveContentType(string? contentType, string? fileNameOrUrl)
    {
        var normalizedContentType = NormalizeOptional(contentType)?.Split(';', 2)[0].Trim().ToLowerInvariant();
        if (string.Equals(normalizedContentType, "image/jpg", StringComparison.Ordinal))
        {
            normalizedContentType = "image/jpeg";
        }

        if (normalizedContentType is not null
            && SupportedContentTypes.Contains(normalizedContentType, StringComparer.Ordinal))
        {
            return normalizedContentType;
        }

        var path = NormalizeOptional(fileNameOrUrl);
        if (path is null)
        {
            return null;
        }

        if (Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri))
        {
            path = absoluteUri.AbsolutePath;
        }

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => null
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed record FlyerImportUploadParseResult(
    bool HasFile,
    UploadedFlyerImportFile? File,
    IReadOnlyCollection<string> Errors)
{
    public bool Succeeded => Errors.Count == 0;

    public static FlyerImportUploadParseResult NoFile()
    {
        return new FlyerImportUploadParseResult(false, null, []);
    }

    public static FlyerImportUploadParseResult Success(UploadedFlyerImportFile file)
    {
        return new FlyerImportUploadParseResult(true, file, []);
    }

    public static FlyerImportUploadParseResult Failure(params string[] errors)
    {
        return new FlyerImportUploadParseResult(true, null, errors);
    }
}
