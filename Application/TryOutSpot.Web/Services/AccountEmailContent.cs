using System.Net;
using TryOutSpot.Web.Data.Entities;

namespace TryOutSpot.Web.Services;

public sealed record AccountEmailContent(string Subject, string Text, string Html);

public static class AccountEmailContentBuilder
{
    public static AccountEmailContent BuildEmailConfirmation(User user, string confirmationToken, AccountEmailOptions options)
    {
        var actionUrl = BuildActionUrl(options, "/account/verify-email", user.Email, confirmationToken);
        var displayName = GetDisplayName(user);
        var subject = "Verify your TryOutSpot email";
        var text = actionUrl is null
            ? $"Hi {displayName}, use this verification token to verify your TryOutSpot email: {confirmationToken}"
            : $"Hi {displayName}, verify your TryOutSpot email here: {actionUrl}";

        var htmlAction = actionUrl is null
            ? $"<p>Use this verification token:</p><p><strong>{WebUtility.HtmlEncode(confirmationToken)}</strong></p>"
            : $"<p><a href=\"{WebUtility.HtmlEncode(actionUrl)}\">Verify your email</a></p>";

        var html = $"""
            <p>Hi {WebUtility.HtmlEncode(displayName)},</p>
            <p>Welcome to TryOutSpot. Please verify your email address to finish setting up your account.</p>
            {htmlAction}
            <p>If you did not create this account, you can ignore this email.</p>
            """;

        return new AccountEmailContent(subject, text, html);
    }

    public static AccountEmailContent BuildPasswordReset(User user, string resetToken, AccountEmailOptions options)
    {
        var actionUrl = BuildActionUrl(options, "/account/reset-password", user.Email, resetToken);
        var displayName = GetDisplayName(user);
        var subject = "Reset your TryOutSpot password";
        var text = actionUrl is null
            ? $"Hi {displayName}, use this password reset token to reset your TryOutSpot password: {resetToken}"
            : $"Hi {displayName}, reset your TryOutSpot password here: {actionUrl}";

        var htmlAction = actionUrl is null
            ? $"<p>Use this password reset token:</p><p><strong>{WebUtility.HtmlEncode(resetToken)}</strong></p>"
            : $"<p><a href=\"{WebUtility.HtmlEncode(actionUrl)}\">Reset your password</a></p>";

        var html = $"""
            <p>Hi {WebUtility.HtmlEncode(displayName)},</p>
            <p>We received a request to reset your TryOutSpot password.</p>
            {htmlAction}
            <p>If you did not request this reset, you can ignore this email.</p>
            """;

        return new AccountEmailContent(subject, text, html);
    }

    private static string GetDisplayName(User user)
    {
        var fullName = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(fullName) ? "there" : fullName;
    }

    private static string? BuildActionUrl(
        AccountEmailOptions options,
        string path,
        string? email,
        string token)
    {
        if (string.IsNullOrWhiteSpace(options.FrontendBaseUrl) || string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var baseUrl = options.FrontendBaseUrl.TrimEnd('/');
        return $"{baseUrl}{path}?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
    }
}
