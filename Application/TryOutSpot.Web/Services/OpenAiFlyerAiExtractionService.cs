using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace TryOutSpot.Web.Services;

public sealed class OpenAiFlyerAiExtractionService(
    HttpClient httpClient,
    IOptions<OpenAiOptions> options,
    ILogger<OpenAiFlyerAiExtractionService> logger) : IFlyerAiExtractionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<FlyerAiExtractionResult> ExtractAsync(
        UploadedFlyerImportFile uploadedFile,
        string? sourceUrl,
        string? externalImageUrl,
        CancellationToken cancellationToken)
    {
        var openAiOptions = options.Value;
        if (!openAiOptions.IsConfigured)
        {
            return FlyerAiExtractionResult.Failure("OpenAI flyer extraction is not configured yet.");
        }

        if (!uploadedFile.ContentType.StartsWith("image/", StringComparison.Ordinal))
        {
            return FlyerAiExtractionResult.Failure("AI flyer extraction currently supports image flyers. Upload a JPG, PNG, or WEBP image.");
        }

        var endpoint = openAiOptions.UsesChatCompletions
            ? openAiOptions.ResolveChatCompletionsEndpoint()
            : openAiOptions.ResolveResponsesEndpoint();
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", openAiOptions.ApiKey);
        var requestPayload = openAiOptions.UsesChatCompletions
            ? BuildChatCompletionsPayload(
                openAiOptions.FlyerExtractionModel,
                uploadedFile,
                sourceUrl,
                externalImageUrl,
                openAiOptions.DisableThinking)
            : BuildResponsesPayload(openAiOptions.FlyerExtractionModel, uploadedFile, sourceUrl, externalImageUrl);
        request.Content = JsonContent.Create(
            requestPayload,
            options: JsonOptions);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "OpenAI flyer extraction failed. StatusCode={StatusCode} Response={ResponseBody}",
                (int)response.StatusCode,
                responseBody);
            return FlyerAiExtractionResult.Failure("AI could not read the flyer right now. Check OpenAI configuration and try again.");
        }

        var outputText = ExtractOutputText(responseBody);
        if (string.IsNullOrWhiteSpace(outputText))
        {
            logger.LogWarning("OpenAI flyer extraction returned no output text. Response={ResponseBody}", responseBody);
            return FlyerAiExtractionResult.Failure("AI did not return flyer details. Try a clearer image.");
        }

        FlyerExtractionPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<FlyerExtractionPayload>(outputText, JsonOptions);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "OpenAI flyer extraction returned invalid JSON. Output={OutputText}", outputText);
            return FlyerAiExtractionResult.Failure("AI returned flyer details in an unexpected format. Try again.");
        }

        if (payload is null)
        {
            return FlyerAiExtractionResult.Failure("AI did not return flyer details. Try a clearer image.");
        }

        var today = DateTime.UtcNow.Date;
        var confidenceJson = JsonSerializer.Serialize(
            new
            {
                payload.ConfidenceScore,
                payload.Warnings,
                payload.EventDateYearSpecified,
                payload.EventEndDateYearSpecified,
                payload.RegistrationDeadlineYearSpecified,
                CurrentYearApplied = today.Year
            },
            JsonOptions);
        var input = new FlyerImportCreateInput(
            "facebook",
            sourceUrl,
            externalImageUrl,
            SportId: null,
            payload.SportName,
            payload.OpportunityType,
            payload.Title,
            payload.TeamName,
            payload.OrganizationName,
            payload.AgeGroup,
            payload.CompetitionLevel,
            ParseDate(payload.EventDate, payload.EventDateYearSpecified, today),
            ParseDate(payload.EventEndDate, payload.EventEndDateYearSpecified, today),
            ParseDate(payload.RegistrationDeadline, payload.RegistrationDeadlineYearSpecified, today),
            payload.RegistrationFee,
            payload.Location,
            payload.Address,
            payload.City,
            payload.State,
            payload.ZipCode,
            payload.ContactEmail,
            payload.ContactPhone,
            payload.WebsiteUrl,
            payload.Description,
            payload.RequiredEquipment,
            payload.WhatToBring,
            payload.SpecialInstructions,
            outputText,
            confidenceJson,
            payload.Warnings);

        return FlyerAiExtractionResult.Success(input, outputText, confidenceJson);
    }

    private static object BuildResponsesPayload(
        string model,
        UploadedFlyerImportFile uploadedFile,
        string? sourceUrl,
        string? externalImageUrl)
    {
        var today = DateTime.UtcNow.Date;
        var imageDataUrl = $"data:{uploadedFile.ContentType};base64,{Convert.ToBase64String(uploadedFile.Content)}";
        return new
        {
            model,
            input = new object[]
            {
                new
                {
                    role = "system",
                    content = new object[]
                    {
                        new
                        {
                            type = "input_text",
                            text = BuildSystemPrompt(today)
                        }
                    }
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "input_text",
                            text = BuildUserPrompt(today, sourceUrl, externalImageUrl)
                        },
                        new
                        {
                            type = "input_image",
                            image_url = imageDataUrl,
                            detail = "high"
                        }
                    }
                }
            },
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "flyer_import_extraction",
                    strict = true,
                    schema = BuildSchema()
                }
            },
            max_output_tokens = 2500
        };
    }

    private static object BuildChatCompletionsPayload(
        string model,
        UploadedFlyerImportFile uploadedFile,
        string? sourceUrl,
        string? externalImageUrl,
        bool disableThinking)
    {
        var today = DateTime.UtcNow.Date;
        var imageDataUrl = $"data:{uploadedFile.ContentType};base64,{Convert.ToBase64String(uploadedFile.Content)}";
        return new
        {
            model,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = BuildSystemPrompt(today)
                },
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "text",
                            text = BuildUserPrompt(today, sourceUrl, externalImageUrl)
                        },
                        new
                        {
                            type = "image_url",
                            image_url = new
                            {
                                url = imageDataUrl
                            }
                        }
                    }
                }
            },
            response_format = new
            {
                type = "json_object"
            },
            temperature = 0,
            max_tokens = 2500,
            chat_template_kwargs = disableThinking
                ? new { enable_thinking = false }
                : null
        };
    }

    private static string BuildSystemPrompt(DateTime today)
    {
        return $"You extract youth baseball and softball opportunity listings from flyer images. Return JSON only and leave unknown fields null. Today's date is {today:yyyy-MM-dd}; the current year is {today.Year}. When a flyer shows a month/day date without a printed year, use {today.Year}. Only use a different year when that year is explicitly printed on the flyer. Set the date yearSpecified fields to true only when the flyer visibly prints a year for that date. Normalize opportunityType to tryout, roster_opening, pickup_player, tournament, camp, clinic, private_workout, or other. Use roster_opening when the flyer says adding players or looking for players. Use pickup_player when it says guest player, sub, fill-in, or pickup player. Put venue, complex, park, or field names in location even when no street address is visible. Put city and state in city/state when visible, but do not guess a ZIP code. Return a JSON object with these keys: title, teamName, organizationName, sportName, opportunityType, ageGroup, competitionLevel, eventDate, eventDateYearSpecified, eventEndDate, eventEndDateYearSpecified, registrationDeadline, registrationDeadlineYearSpecified, registrationFee, location, address, city, state, zipCode, contactEmail, contactPhone, websiteUrl, description, requiredEquipment, whatToBring, specialInstructions, confidenceScore, warnings.";
    }

    private static string BuildUserPrompt(
        DateTime today,
        string? sourceUrl,
        string? externalImageUrl)
    {
        return $"Extract listing information from this flyer image. Source post URL: {sourceUrl ?? "not provided"}. External image URL: {externalImageUrl ?? "not provided"}. Dates should be ISO-8601 if visible. If the flyer does not print a year on a date, use {today.Year}; do not infer an older year. Include the ZIP code only if visible. Return JSON only.";
    }

    private static JsonObject BuildSchema()
    {
        var properties = new JsonObject
        {
            ["title"] = NullableString(),
            ["teamName"] = NullableString(),
            ["organizationName"] = NullableString(),
            ["sportName"] = NullableString(),
            ["opportunityType"] = NullableString(),
            ["ageGroup"] = NullableString(),
            ["competitionLevel"] = NullableString(),
            ["eventDate"] = NullableString(),
            ["eventDateYearSpecified"] = NullableBoolean(),
            ["eventEndDate"] = NullableString(),
            ["eventEndDateYearSpecified"] = NullableBoolean(),
            ["registrationDeadline"] = NullableString(),
            ["registrationDeadlineYearSpecified"] = NullableBoolean(),
            ["registrationFee"] = new JsonObject { ["type"] = new JsonArray("number", "null") },
            ["location"] = NullableString(),
            ["address"] = NullableString(),
            ["city"] = NullableString(),
            ["state"] = NullableString(),
            ["zipCode"] = NullableString(),
            ["contactEmail"] = NullableString(),
            ["contactPhone"] = NullableString(),
            ["websiteUrl"] = NullableString(),
            ["description"] = NullableString(),
            ["requiredEquipment"] = NullableString(),
            ["whatToBring"] = NullableString(),
            ["specialInstructions"] = NullableString(),
            ["confidenceScore"] = new JsonObject { ["type"] = new JsonArray("number", "null") },
            ["warnings"] = NullableString()
        };

        var required = new JsonArray();
        foreach (var property in properties)
        {
            required.Add(property.Key);
        }

        return new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = properties,
            ["required"] = required
        };
    }

    private static JsonObject NullableString()
    {
        return new JsonObject { ["type"] = new JsonArray("string", "null") };
    }

    private static JsonObject NullableBoolean()
    {
        return new JsonObject { ["type"] = new JsonArray("boolean", "null") };
    }

    private static string? ExtractOutputText(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        if (document.RootElement.TryGetProperty("output_text", out var outputTextElement)
            && outputTextElement.ValueKind == JsonValueKind.String)
        {
            return outputTextElement.GetString();
        }

        if (document.RootElement.TryGetProperty("choices", out var choicesElement)
            && choicesElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var choiceElement in choicesElement.EnumerateArray())
            {
                if (choiceElement.TryGetProperty("message", out var messageElement)
                    && messageElement.TryGetProperty("content", out var contentElement)
                    && contentElement.ValueKind == JsonValueKind.String)
                {
                    return contentElement.GetString();
                }
            }
        }

        if (!document.RootElement.TryGetProperty("output", out var outputElement)
            || outputElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var outputItem in outputElement.EnumerateArray())
        {
            if (!outputItem.TryGetProperty("content", out var contentElement)
                || contentElement.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in contentElement.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out var textElement)
                    && textElement.ValueKind == JsonValueKind.String)
                {
                    return textElement.GetString();
                }
            }
        }

        return null;
    }

    private static DateTime? ParseDate(string? value, bool? yearSpecified, DateTime today)
    {
        if (!DateTime.TryParse(value, out var parsed))
        {
            return null;
        }

        if (yearSpecified == false && parsed.Year != today.Year)
        {
            var day = Math.Min(parsed.Day, DateTime.DaysInMonth(today.Year, parsed.Month));
            return new DateTime(
                today.Year,
                parsed.Month,
                day,
                parsed.Hour,
                parsed.Minute,
                parsed.Second,
                parsed.Millisecond,
                parsed.Kind);
        }

        return parsed;
    }

    private sealed class FlyerExtractionPayload
    {
        public string? Title { get; set; }

        public string? TeamName { get; set; }

        public string? OrganizationName { get; set; }

        public string? SportName { get; set; }

        public string? OpportunityType { get; set; }

        public string? AgeGroup { get; set; }

        public string? CompetitionLevel { get; set; }

        public string? EventDate { get; set; }

        public bool? EventDateYearSpecified { get; set; }

        public string? EventEndDate { get; set; }

        public bool? EventEndDateYearSpecified { get; set; }

        public string? RegistrationDeadline { get; set; }

        public bool? RegistrationDeadlineYearSpecified { get; set; }

        public decimal? RegistrationFee { get; set; }

        public string? Location { get; set; }

        public string? Address { get; set; }

        public string? City { get; set; }

        public string? State { get; set; }

        public string? ZipCode { get; set; }

        public string? ContactEmail { get; set; }

        public string? ContactPhone { get; set; }

        public string? WebsiteUrl { get; set; }

        public string? Description { get; set; }

        public string? RequiredEquipment { get; set; }

        public string? WhatToBring { get; set; }

        public string? SpecialInstructions { get; set; }

        public decimal? ConfidenceScore { get; set; }

        [JsonConverter(typeof(WarningTextJsonConverter))]
        public string? Warnings { get; set; }
    }

    private sealed class WarningTextJsonConverter : JsonConverter<string?>
    {
        public override string? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                return reader.GetString();
            }

            if (reader.TokenType != JsonTokenType.StartArray)
            {
                using var valueDocument = JsonDocument.ParseValue(ref reader);
                return valueDocument.RootElement.GetRawText();
            }

            using var arrayDocument = JsonDocument.ParseValue(ref reader);
            var warnings = arrayDocument.RootElement
                .EnumerateArray()
                .Select(warningElement => warningElement.ValueKind == JsonValueKind.String
                    ? warningElement.GetString()
                    : warningElement.GetRawText())
                .Where(warning => !string.IsNullOrWhiteSpace(warning))
                .ToArray();

            return warnings.Length == 0
                ? null
                : string.Join("; ", warnings);
        }

        public override void Write(
            Utf8JsonWriter writer,
            string? value,
            JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStringValue(value);
        }
    }
}
