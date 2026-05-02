using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using TryOutSpot.Web.Security;

namespace TryOutSpot.Web.Services;

public interface IExternalLoginTicketService
{
    string Create(ExternalLoginTicket ticket);

    bool TryRead(string protectedToken, out ExternalLoginTicket ticket);
}

public sealed record ExternalLoginTicket(
    string Provider,
    string ProviderKey,
    string Email,
    bool EmailVerified,
    string? FirstName,
    string? LastName,
    string? ProfileImageUrl = null);

public sealed class ExternalLoginTicketService : IExternalLoginTicketService
{
    private readonly ITimeLimitedDataProtector protector;
    private readonly TimeSpan tokenLifetime;

    public ExternalLoginTicketService(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<SocialLoginOptions> options)
    {
        protector = dataProtectionProvider
            .CreateProtector("TryOutSpot.Web.ExternalLoginTicket.v1")
            .ToTimeLimitedDataProtector();
        tokenLifetime = TimeSpan.FromMinutes(Math.Clamp(options.Value.ExternalLoginTokenMinutes, 1, 60));
    }

    public string Create(ExternalLoginTicket ticket)
    {
        var json = JsonSerializer.Serialize(ticket);
        return protector.Protect(json, tokenLifetime);
    }

    public bool TryRead(string protectedToken, out ExternalLoginTicket ticket)
    {
        ticket = new ExternalLoginTicket(string.Empty, string.Empty, string.Empty, false, null, null);

        if (string.IsNullOrWhiteSpace(protectedToken))
        {
            return false;
        }

        try
        {
            var json = protector.Unprotect(protectedToken);
            var parsedTicket = JsonSerializer.Deserialize<ExternalLoginTicket>(json);
            if (parsedTicket is null
                || string.IsNullOrWhiteSpace(parsedTicket.Provider)
                || string.IsNullOrWhiteSpace(parsedTicket.ProviderKey)
                || string.IsNullOrWhiteSpace(parsedTicket.Email))
            {
                return false;
            }

            ticket = parsedTicket;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
