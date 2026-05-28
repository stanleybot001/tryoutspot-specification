using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Tests;

public sealed class TwilioAccountSmsSenderTests
{
    [Fact]
    public async Task SendPhoneVerificationCodeAsync_UsesApprovedMessagingServiceAndBody()
    {
        var handler = new CapturingTwilioHandler();
        using var httpClient = new HttpClient(handler);
        var sender = new TwilioAccountSmsSender(
            httpClient,
            Options.Create(new TwilioSmsOptions
            {
                AccountSid = "AC1234567890",
                AuthToken = "test-token",
                FromPhoneNumber = "+15555550100",
                MessagingServiceSid = "MGfbbcc11db84f296622035a37e57a6de6"
            }),
            NullLogger<TwilioAccountSmsSender>.Instance);
        var user = new User
        {
            Id = Guid.NewGuid(),
            FirstName = "Test",
            LastName = "User",
            Email = "sms-sender@example.com",
            UserName = "sms-sender@example.com"
        };

        await sender.SendPhoneVerificationCodeAsync(user, "+19135550100", "123456", CancellationToken.None);

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal(
            "https://api.twilio.com/2010-04-01/Accounts/AC1234567890/Messages.json",
            handler.RequestUri?.ToString());
        Assert.Equal("Basic", handler.AuthorizationScheme);
        Assert.NotNull(handler.FormValues);
        Assert.Equal("+19135550100", handler.FormValues["To"].ToString());
        Assert.Equal("MGfbbcc11db84f296622035a37e57a6de6", handler.FormValues["MessagingServiceSid"].ToString());
        Assert.False(handler.FormValues.ContainsKey("From"));
        Assert.Equal(
            "TryOutSpot: Your verification code is 123456. Reply STOP to opt out, HELP for help.",
            handler.FormValues["Body"].ToString());
    }

    private sealed class CapturingTwilioHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? AuthorizationScheme { get; private set; }

        public Dictionary<string, StringValues>? FormValues { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            var formBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            FormValues = QueryHelpers
                .ParseQuery(formBody)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent("{}")
            };
        }
    }
}
