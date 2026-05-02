using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TryOutSpot.Web.Data.Entities;

namespace TryOutSpot.Web.Services;

public sealed class ResendAccountEmailSender(
    HttpClient httpClient,
    IOptions<AccountEmailOptions> emailOptions,
    IOptions<ResendEmailOptions> resendOptions,
    ILogger<ResendAccountEmailSender> logger) : IAccountEmailSender
{
    public Task SendEmailConfirmationTokenAsync(
        User user,
        string confirmationToken,
        CancellationToken cancellationToken)
    {
        var content = AccountEmailContentBuilder.BuildEmailConfirmation(
            user,
            confirmationToken,
            emailOptions.Value);

        return SendAsync(user, content, cancellationToken);
    }

    public Task SendPasswordResetTokenAsync(
        User user,
        string resetToken,
        CancellationToken cancellationToken)
    {
        var content = AccountEmailContentBuilder.BuildPasswordReset(
            user,
            resetToken,
            emailOptions.Value);

        return SendAsync(user, content, cancellationToken);
    }

    private async Task SendAsync(
        User user,
        AccountEmailContent content,
        CancellationToken cancellationToken)
    {
        var options = emailOptions.Value;
        var resend = resendOptions.Value;
        var recipient = user.Email;

        if (string.IsNullOrWhiteSpace(recipient))
        {
            throw new InvalidOperationException("A recipient email address is required.");
        }

        if (string.IsNullOrWhiteSpace(options.FromEmail))
        {
            throw new InvalidOperationException("Email:FromEmail must be configured before using Resend.");
        }

        if (string.IsNullOrWhiteSpace(resend.ApiKey) || resend.ApiKey.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Resend:ApiKey must be configured before using Resend.");
        }

        if (string.IsNullOrWhiteSpace(resend.Endpoint))
        {
            throw new InvalidOperationException("Resend:Endpoint must be configured before using Resend.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, resend.Endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", resend.ApiKey);
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
        request.Content = JsonContent.Create(new ResendSendEmailRequest(
            FormatSender(options),
            [recipient],
            content.Subject,
            content.Html,
            content.Text));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            logger.LogInformation("Sent TryOutSpot account email to user {UserId} with Resend.", user.Id);
            return;
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogError(
            "Resend email send failed for user {UserId}. Status {StatusCode}. Response: {ResponseBody}",
            user.Id,
            (int)response.StatusCode,
            responseBody);

        response.EnsureSuccessStatusCode();
    }

    private static string FormatSender(AccountEmailOptions options)
    {
        return string.IsNullOrWhiteSpace(options.FromName)
            ? options.FromEmail.Trim()
            : $"{options.FromName.Trim()} <{options.FromEmail.Trim()}>";
    }

    private sealed record ResendSendEmailRequest(
        [property: JsonPropertyName("from")] string From,
        [property: JsonPropertyName("to")] IReadOnlyCollection<string> To,
        [property: JsonPropertyName("subject")] string Subject,
        [property: JsonPropertyName("html")] string Html,
        [property: JsonPropertyName("text")] string Text);
}
