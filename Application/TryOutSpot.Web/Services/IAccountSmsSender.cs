using TryOutSpot.Web.Data.Entities;

namespace TryOutSpot.Web.Services;

public interface IAccountSmsSender
{
    Task SendPhoneVerificationCodeAsync(
        User user,
        string phoneNumber,
        string verificationCode,
        CancellationToken cancellationToken);
}
