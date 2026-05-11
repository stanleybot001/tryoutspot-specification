using TryOutSpot.Web.Data.Entities;

namespace TryOutSpot.Web.Services;

public sealed class LoggingAccountEmailSender(ILogger<LoggingAccountEmailSender> logger) : IAccountEmailSender
{
    public Task SendEmailConfirmationTokenAsync(User user, string confirmationToken, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Email confirmation token generated for user {UserId}. Configure a real email sender before production. Token: {ConfirmationToken}",
            user.Id,
            confirmationToken);

        return Task.CompletedTask;
    }

    public Task SendEmailChangeTokenAsync(
        User user,
        string newEmail,
        string changeToken,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Email change token generated for user {UserId} and new email {NewEmail}. Configure a real email sender before production. Token: {ChangeToken}",
            user.Id,
            newEmail,
            changeToken);

        return Task.CompletedTask;
    }

    public Task SendPasswordResetTokenAsync(User user, string resetToken, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Password reset token generated for user {UserId}. Configure a real email/SMS sender before production. Token: {ResetToken}",
            user.Id,
            resetToken);

        return Task.CompletedTask;
    }
}
