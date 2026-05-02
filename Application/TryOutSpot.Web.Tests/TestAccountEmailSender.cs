using System.Collections.Concurrent;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Tests;

public sealed class TestAccountEmailSender : IAccountEmailSender
{
    private readonly ConcurrentDictionary<string, string> resetTokens = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> emailConfirmationTokens = new(StringComparer.OrdinalIgnoreCase);

    public Task SendEmailConfirmationTokenAsync(User user, string confirmationToken, CancellationToken cancellationToken)
    {
        emailConfirmationTokens[user.Email ?? string.Empty] = confirmationToken;
        return Task.CompletedTask;
    }

    public Task SendPasswordResetTokenAsync(User user, string resetToken, CancellationToken cancellationToken)
    {
        resetTokens[user.Email ?? string.Empty] = resetToken;
        return Task.CompletedTask;
    }

    public bool TryGetPasswordResetToken(string email, out string token)
    {
        return resetTokens.TryGetValue(email, out token!);
    }

    public bool TryGetEmailConfirmationToken(string email, out string token)
    {
        return emailConfirmationTokens.TryGetValue(email, out token!);
    }
}
