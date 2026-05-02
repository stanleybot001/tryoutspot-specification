using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;
using TryOutSpot.Web.Data.Entities;

namespace TryOutSpot.Web.Services;

public sealed class TwilioAccountSmsSender(
    HttpClient httpClient,
    IOptions<TwilioSmsOptions> twilioOptions,
    ILogger<TwilioAccountSmsSender> logger) : IAccountSmsSender
{
    public async Task SendPhoneVerificationCodeAsync(
        User user,
        string phoneNumber,
        string verificationCode,
        CancellationToken cancellationToken)
    {
        var options = twilioOptions.Value;
        EnsureConfigured(options);

        var endpoint = $"{options.EndpointBaseUrl.TrimEnd('/')}/Accounts/{Uri.EscapeDataString(options.AccountSid.Trim())}/Messages.json";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{options.AccountSid.Trim()}:{options.AuthToken}")));

        var formValues = new Dictionary<string, string>
        {
            ["To"] = phoneNumber,
            ["Body"] = $"Your TryOutSpot verification code is {verificationCode}."
        };

        if (string.IsNullOrWhiteSpace(options.MessagingServiceSid))
        {
            formValues["From"] = options.FromPhoneNumber.Trim();
        }
        else
        {
            formValues["MessagingServiceSid"] = options.MessagingServiceSid.Trim();
        }

        request.Content = new FormUrlEncodedContent(formValues);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            logger.LogInformation("Sent TryOutSpot phone verification SMS to user {UserId} with Twilio.", user.Id);
            return;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogError(
            "Twilio SMS send failed for user {UserId}. Status {StatusCode}. Response: {ResponseBody}",
            user.Id,
            (int)response.StatusCode,
            responseBody);

        response.EnsureSuccessStatusCode();
    }

    private static void EnsureConfigured(TwilioSmsOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.AccountSid) || options.AccountSid.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Twilio:AccountSid must be configured before using Twilio.");
        }

        if (string.IsNullOrWhiteSpace(options.AuthToken) || options.AuthToken.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Twilio:AuthToken must be configured before using Twilio.");
        }

        if (string.IsNullOrWhiteSpace(options.EndpointBaseUrl))
        {
            throw new InvalidOperationException("Twilio:EndpointBaseUrl must be configured before using Twilio.");
        }

        if (string.IsNullOrWhiteSpace(options.MessagingServiceSid) && string.IsNullOrWhiteSpace(options.FromPhoneNumber))
        {
            throw new InvalidOperationException("Twilio:MessagingServiceSid or Twilio:FromPhoneNumber must be configured before using Twilio.");
        }
    }
}
