# Admin Center

Implemented an MVC admin center at `/admin` for platform administrators.

## Access

- Requires web cookie authentication.
- Requires the `PlatformAdmin` role.
- The shared site navigation shows an Admin link only to authenticated platform admins.

## Screens

- Public player listing pages include a server-rendered report form for signed-in users who do not own the listing.
- Public team opportunity pages include a server-rendered report form for signed-in users who do not manage the team.
- `/admin` shows moderation and platform totals.
- `/admin/reports` lists reported player listings and team opportunities with filters.
- `/admin/reports/{reportId}` shows report details, reporter profile context, listing context, and a review form.
- `/admin/users` lists users with account type, active state, and profile/listing counts.
- `/admin/users/{userId}` shows a user's account types, player profiles, player listings, teams, and team opportunities.
- `/admin/player-listings` lists all player-side listings with owner and report counts.
- `/admin/team-opportunities` lists all team opportunity listings with owner team and report counts.

## Bootstrap Admin

Admin seeding is configuration driven so credentials are never committed.

Set local environment variables or user secrets:

```powershell
$env:BootstrapAdmin__Email = "admin@example.com"
$env:BootstrapAdmin__Password = "local-admin-password"
$env:BootstrapAdmin__FirstName = "Platform"
$env:BootstrapAdmin__LastName = "Admin"
$env:BootstrapAdmin__ResetPassword = "false"
dotnet run --project Application\TryOutSpot.Web\TryOutSpot.Web.csproj -- --seed-admin
```

The seeder ensures the `PlatformAdmin` role exists, creates the configured user if missing, confirms the email, reactivates the account, and assigns the role. It only resets an existing password when `BootstrapAdmin:ResetPassword` is set to `true`.
