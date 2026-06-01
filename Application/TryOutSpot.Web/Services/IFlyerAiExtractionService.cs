namespace TryOutSpot.Web.Services;

public interface IFlyerAiExtractionService
{
    Task<FlyerAiExtractionResult> ExtractAsync(
        UploadedFlyerImportFile uploadedFile,
        string? sourceUrl,
        string? externalImageUrl,
        CancellationToken cancellationToken);
}

public sealed record FlyerAiExtractionResult(
    bool Succeeded,
    FlyerImportCreateInput? Input,
    string? ExtractedJson,
    string? ConfidenceJson,
    IReadOnlyCollection<string> Errors)
{
    public static FlyerAiExtractionResult Success(
        FlyerImportCreateInput input,
        string extractedJson,
        string? confidenceJson)
    {
        return new FlyerAiExtractionResult(true, input, extractedJson, confidenceJson, []);
    }

    public static FlyerAiExtractionResult Failure(params string[] errors)
    {
        return new FlyerAiExtractionResult(false, null, null, null, errors);
    }
}
