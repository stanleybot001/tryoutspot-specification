namespace TryOutSpot.Web.Services;

public sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    public string ApiKey { get; set; } = string.Empty;

    public string ApiMode { get; set; } = "responses";

    public string ResponsesEndpoint { get; set; } = "https://api.openai.com/v1/responses";

    public string ChatCompletionsEndpoint { get; set; } = string.Empty;

    public string FlyerExtractionModel { get; set; } = "gpt-5.4-mini";

    public bool DisableThinking { get; set; }

    public bool UsesChatCompletions =>
        string.Equals(ApiMode, "chat_completions", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ApiMode, "chat-completions", StringComparison.OrdinalIgnoreCase)
        || string.Equals(ApiMode, "chat", StringComparison.OrdinalIgnoreCase);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey)
        && !ApiKey.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase)
        && !ApiKey.StartsWith("PUT_", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(UsesChatCompletions ? ResolveChatCompletionsEndpoint() : ResolveResponsesEndpoint())
        && !string.IsNullOrWhiteSpace(FlyerExtractionModel);

    public string ResolveResponsesEndpoint()
    {
        return ResolveEndpoint(ResponsesEndpoint, "responses");
    }

    public string ResolveChatCompletionsEndpoint()
    {
        return !string.IsNullOrWhiteSpace(ChatCompletionsEndpoint)
            ? ChatCompletionsEndpoint
            : ResolveEndpoint(ResponsesEndpoint, "chat/completions");
    }

    private static string ResolveEndpoint(string configuredEndpoint, string suffix)
    {
        if (string.IsNullOrWhiteSpace(configuredEndpoint))
        {
            return string.Empty;
        }

        var endpoint = configuredEndpoint.TrimEnd('/');
        if (endpoint.EndsWith($"/{suffix}", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        if (endpoint.EndsWith("/responses", StringComparison.OrdinalIgnoreCase)
            || endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            var versionRoot = endpoint[..endpoint.LastIndexOf("/", StringComparison.Ordinal)];
            if (endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                versionRoot = versionRoot[..versionRoot.LastIndexOf("/", StringComparison.Ordinal)];
            }

            return $"{versionRoot}/{suffix}";
        }

        return $"{endpoint}/{suffix}";
    }
}
