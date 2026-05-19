# Facebook Login Remote Failure Fix - 2026-05-19

Facebook Login reached the Meta consent screen but returned to TryOutSpot with
`externalLoginStatus=failed`. The failure happened inside the Facebook
authentication middleware before the account callback could create or link a
TryOutSpot user.

The app was explicitly requesting the `verified` and `is_verified` Facebook user
profile fields. Those fields are not needed for TryOutSpot sign-in, and asking
for extra user fields can cause the Facebook profile request to fail after the
OAuth token exchange. The Facebook auth handler already requests the basic
fields TryOutSpot needs: `name`, `email`, `first_name`, and `last_name`.

Changes made:

- Removed the extra Facebook verification profile fields.
- Kept first and last name mapping from the default Facebook profile response.
- Added remote-failure logging for Google and Facebook social login callbacks.
- Added a regression test so the Facebook options do not request the removed
  verification fields.

Verification:

- `dotnet build TryOutSpot.sln --verbosity minimal`
- `dotnet test Application\TryOutSpot.Web.Tests\TryOutSpot.Web.Tests.csproj --verbosity minimal`
