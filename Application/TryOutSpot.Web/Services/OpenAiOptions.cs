namespace TryOutSpot.Web.Services;

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    public string ApiKey { get; set; } = string.Empty;

    public string ResponsesEndpoint { get; set; } = "https://api.openai.com/v1/responses";

    public string FlyerExtractionModel { get; set; } = "gpt-5.4-mini";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !ApiKey.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
        && !ApiKey.StartsWith("PUT_", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(ResponsesEndpoint)
        && !string.IsNullOrWhiteSpace(FlyerExtractionModel);
}
