using System.Collections.Concurrent;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Tests;

public sealed class TestAccountEmailSender : IAccountEmailSender
{
    private readonly ConcurrentDictionary<string, string> resetTokens = new(StringComparer.OrdinalIgnoreCase);

    public Task SendPasswordResetTokenAsync(User user, string resetToken, CancellationToken cancellationToken)
    {
        resetTokens[user.Email ?? string.Empty] = resetToken;
        return Task.CompletedTask;
    }

    public bool TryGetToken(string email, out string token)
    {
        return resetTokens.TryGetValue(email, out token!);
    }
}
