using TryOutSpot.Web.Data.Entities;

namespace TryOutSpot.Web.Services;

public interface IAccountEmailSender
{
    Task SendEmailConfirmationTokenAsync(User user, string confirmationToken, CancellationToken cancellationToken);

    Task SendPasswordResetTokenAsync(User user, string resetToken, CancellationToken cancellationToken);
}
