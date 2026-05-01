using TryOutSpot.Web.Data.Entities;

namespace TryOutSpot.Web.Services;

public sealed class LoggingAccountEmailSender(ILogger<LoggingAccountEmailSender> logger) : IAccountEmailSender
{
    public Task SendPasswordResetTokenAsync(User user, string resetToken, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Password reset token generated for user {UserId}. Configure a real email/SMS sender before production. Token: {ResetToken}",
            user.Id,
            resetToken);

        return Task.CompletedTask;
    }
}
