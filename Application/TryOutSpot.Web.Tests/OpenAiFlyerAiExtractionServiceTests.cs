using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Tests;

public sealed class OpenAiFlyerAiExtractionServiceTests
{
    [Fact]
    public async Task ExtractAsync_UsesCurrentYearWhenFlyerDateDidNotShowYear()
    {
        var currentYear = DateTime.UtcNow.Year;
        const int staleYear = 2024;
        var flyerPayload = $$"""
            {
              "title": "Eagles 12U Softball Tryouts",
              "teamName": "Eagles",
              "organizationName": null,
              "sportName": "Softball",
              "opportunityType": "tryout",
              "ageGroup": "12U",
              "competitionLevel": null,
              "eventDate": "{{staleYear}}-07-06T18:00:00",
              "eventDateYearSpecified": false,
              "eventEndDate": "{{staleYear}}-07-07T20:00:00",
              "eventEndDateYearSpecified": false,
              "registrationDeadline": "{{staleYear}}-07-01",
              "registrationDeadlineYearSpecified": false,
              "registrationFee": null,
              "location": "Capital Federal Sports Complex Liberty Field",
              "address": null,
              "city": "Liberty",
              "state": "MO",
              "zipCode": null,
              "contactEmail": "eagles@example.test",
              "contactPhone": "816-555-2026",
              "websiteUrl": null,
              "description": "All positions welcome.",
              "requiredEquipment": null,
              "whatToBring": null,
              "specialInstructions": null,
              "confidenceScore": 0.93,
              "warnings": null
            }
            """;
        var responseBody = JsonSerializer.Serialize(new { output_text = flyerPayload });
        var handler = new StubOpenAiHandler(responseBody);
        var service = new OpenAiFlyerAiExtractionService(
            new HttpClient(handler),
            Options.Create(new OpenAiOptions
            {
                ApiKey = "sk-test",
                ResponsesEndpoint = "https://api.openai.test/v1/responses",
                FlyerExtractionModel = "gpt-test"
            }),
            NullLogger<OpenAiFlyerAiExtractionService>.Instance);

        var result = await service.ExtractAsync(
            new UploadedFlyerImportFile("eagles.jpg", [0xFF, 0xD8, 0xFF, 0xE0], "image/jpeg"),
            sourceUrl: "https://facebook.test/posts/eagles",
            externalImageUrl: null,
            CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        Assert.NotNull(result.Input);
        Assert.Equal(currentYear, result.Input.EventDate?.Year);
        Assert.Equal(7, result.Input.EventDate?.Month);
        Assert.Equal(6, result.Input.EventDate?.Day);
        Assert.Equal(currentYear, result.Input.EventEndDate?.Year);
        Assert.Equal(7, result.Input.EventEndDate?.Day);
        Assert.Equal(currentYear, result.Input.RegistrationDeadline?.Year);
        Assert.Contains($"current year is {currentYear}", handler.RequestBody);
        Assert.Contains("\"eventDateYearSpecified\"", result.ConfidenceJson);
        Assert.Equal("https://api.openai.test/v1/responses", handler.RequestUri?.ToString());
    }

    [Fact]
    public async Task ExtractAsync_CanUseOpenAiCompatibleChatCompletionsEndpoint()
    {
        var flyerPayload = """
            {
              "title": "Mad Batters Softball 16U Seeking Players",
              "teamName": "Mad Batters Softball",
              "organizationName": "Mad Batters Softball Organization",
              "sportName": "Softball",
              "opportunityType": "roster_opening",
              "ageGroup": "16U",
              "competitionLevel": "summer travel",
              "eventDate": null,
              "eventDateYearSpecified": null,
              "eventEndDate": null,
              "eventEndDateYearSpecified": null,
              "registrationDeadline": null,
              "registrationDeadlineYearSpecified": null,
              "registrationFee": null,
              "location": null,
              "address": null,
              "city": "Greenwood",
              "state": "IN",
              "zipCode": null,
              "contactEmail": null,
              "contactPhone": null,
              "websiteUrl": "https://madbatters.org",
              "description": "16U softball team seeking players.",
              "requiredEquipment": null,
              "whatToBring": null,
              "specialInstructions": null,
              "confidenceScore": 0.88,
              "warnings": null
            }
            """;
        var responseBody = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content = flyerPayload
                    }
                }
            }
        });
        var handler = new StubOpenAiHandler(responseBody);
        var service = new OpenAiFlyerAiExtractionService(
            new HttpClient(handler),
            Options.Create(new OpenAiOptions
            {
                ApiKey = "dummy-key-for-compatibility",
                ApiMode = "chat_completions",
                ResponsesEndpoint = "http://local-vllm.test/v1",
                FlyerExtractionModel = "Qwen/Qwen3.5-397B-A17B-GPTQ-Int4",
                DisableThinking = true
            }),
            NullLogger<OpenAiFlyerAiExtractionService>.Instance);

        var result = await service.ExtractAsync(
            new UploadedFlyerImportFile("mad-batters.jpg", [0xFF, 0xD8, 0xFF, 0xE0], "image/jpeg"),
            sourceUrl: "https://facebook.test/posts/mad-batters",
            externalImageUrl: null,
            CancellationToken.None);

        Assert.True(result.Succeeded, string.Join(", ", result.Errors));
        Assert.NotNull(result.Input);
        Assert.Equal("Mad Batters Softball", result.Input.TeamName);
        Assert.Equal("Softball", result.Input.SportName);
        Assert.Equal("roster_opening", result.Input.OpportunityType);
        Assert.Equal("http://local-vllm.test/v1/chat/completions", handler.RequestUri?.ToString());
        Assert.Contains("\"chat_template_kwargs\":{\"enable_thinking\":false}", handler.RequestBody);
        Assert.Contains("\"type\":\"image_url\"", handler.RequestBody);
    }

    private sealed class StubOpenAiHandler(string responseBody) : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = string.Empty;

        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
