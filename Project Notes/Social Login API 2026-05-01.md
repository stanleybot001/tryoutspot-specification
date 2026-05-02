# Social Login API

Date: 2026-05-01

## Scope

Added API-first social login support under `/api/social-login`.

The implementation includes:

- Provider discovery
- External challenge start
- External callback handling
- Short-lived external login token for new social identities
- Social registration with public account type combinations
- Linking social login to an authenticated account
- Listing linked social logins
- Unlinking social login with a last-sign-in-method guard

## Providers

Google and Facebook are wired through official ASP.NET Core authentication handlers:

- `Microsoft.AspNetCore.Authentication.Google`
- `Microsoft.AspNetCore.Authentication.Facebook`

Apple is listed in provider/configuration responses but remains disabled until Apple Developer Service ID, Team ID, Key ID, private key, and server-side token validation are configured.

## Configuration

Tracked files contain placeholders only:

- `Authentication:Google:ClientId`
- `Authentication:Google:ClientSecret`
- `Authentication:Facebook:AppId`
- `Authentication:Facebook:AppSecret`
- `Authentication:Apple:*`

Local development values belong in ignored `Application/TryOutSpot.Web/appsettings.Local.json`.

Google redirect URI:

- Development default: `https://localhost:7079/signin-google`
- Production default: `https://<production-domain>/signin-google`

Facebook redirect URI:

- Development default: `https://localhost:7079/signin-facebook`

## Google Login Completion

Google is the first provider considered production-ready in this codebase.

- Google is considered available only when `Authentication:Google:ClientId` and `Authentication:Google:ClientSecret` are configured with non-placeholder values.
- The configured callback path defaults to `/signin-google`, matching the ASP.NET Core Google handler default and the Google Cloud OAuth redirect URI.
- The API captures Google's `email_verified` field and treats the new TryOutSpot email as confirmed only when Google reports the email as verified.
- The API also captures Google's profile picture URL for future frontend profile use.
- Existing email/password accounts are not linked automatically by matching email. Users must sign in and explicitly link Google from their authenticated account.
- Facebook and Apple/iCloud remain planned follow-up providers.

## Security Notes

- Existing email/password accounts are not auto-linked by email. The user must sign in and link the provider.
- New social registrations are limited to public account types. `PlatformAdmin` cannot be self-selected through social registration.
- External login tokens are protected with ASP.NET Core Data Protection and expire quickly.
- External-only accounts cannot unlink their only sign-in method.
- State-changing endpoints use `POST`; read-only endpoints use `GET`.

## Testing

Integration tests cover provider discovery, social registration, duplicate-email rejection, linking, and last-sign-in-method unlink protection.
