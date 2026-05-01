using TryOutSpot.Web.Data.Entities;

namespace TryOutSpot.Web.Services;

public interface IAccountEmailSender
{
    Task SendPasswordResetTokenAsync(User user, string resetToken, CancellationToken cancellationToken);
}
