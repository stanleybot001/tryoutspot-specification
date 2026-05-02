namespace TryOutSpot.Web.Services;

public static class AccountCommunicationProviders
{
    public const string Logging = "Logging";

    public const string Resend = "Resend";

    public const string Twilio = "Twilio";
}

public sealed class AccountEmailOptions
{
    public const string SectionName = "Email";

    public string Provider { get; set; } = AccountCommunicationProviders.Logging;

    public string FromEmail { get; set; } = "no-reply@tryoutspot.com";

    public string FromName { get; set; } = "TryOutSpot";

    public string FrontendBaseUrl { get; set; } = string.Empty;
}

public sealed class ResendEmailOptions
{
    public const string SectionName = "Resend";

    public string ApiKey { get; set; } = string.Empty;

    public string Endpoint { get; set; } = "https://api.resend.com/emails";
}

public sealed class AccountSmsOptions
{
    public const string SectionName = "Sms";

    public string Provider { get; set; } = AccountCommunicationProviders.Logging;
}

public sealed class TwilioSmsOptions
{
    public const string SectionName = "Twilio";

    public string AccountSid { get; set; } = string.Empty;

    public string AuthToken { get; set; } = string.Empty;

    public string FromPhoneNumber { get; set; } = string.Empty;

    public string MessagingServiceSid { get; set; } = string.Empty;

    public string EndpointBaseUrl { get; set; } = "https://api.twilio.com/2010-04-01";
}
