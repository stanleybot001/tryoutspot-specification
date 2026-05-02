using TryOutSpot.Web.Data.Entities;

namespace TryOutSpot.Web.Services;

public sealed class LoggingAccountSmsSender(ILogger<LoggingAccountSmsSender> logger) : IAccountSmsSender
{
    public Task SendPhoneVerificationCodeAsync(
        User user,
        string phoneNumber,
        string verificationCode,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Phone verification code generated for user {UserId} and phone {PhoneNumber}. Configure a real SMS sender before production. Code: {VerificationCode}",
            user.Id,
            phoneNumber,
            verificationCode);

        return Task.CompletedTask;
    }
}
