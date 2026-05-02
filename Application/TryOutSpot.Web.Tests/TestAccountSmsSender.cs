using System.Collections.Concurrent;
using TryOutSpot.Web.Data.Entities;
using TryOutSpot.Web.Services;

namespace TryOutSpot.Web.Tests;

public sealed class TestAccountSmsSender : IAccountSmsSender
{
    private readonly ConcurrentDictionary<string, string> verificationCodes = new(StringComparer.OrdinalIgnoreCase);

    public Task SendPhoneVerificationCodeAsync(
        User user,
        string phoneNumber,
        string verificationCode,
        CancellationToken cancellationToken)
    {
        verificationCodes[phoneNumber] = verificationCode;
        return Task.CompletedTask;
    }

    public bool TryGetCode(string phoneNumber, out string code)
    {
        return verificationCodes.TryGetValue(phoneNumber, out code!);
    }
}
