# Multiple Subscriptions - 2026-05-12

## Decision

TryOutSpot users can have more than one paid subscription.

This supports real sports-family scenarios:

- A parent may manage multiple child player profiles.
- A parent may also coach a team.
- A player may also be a coach.
- A user may need `premium_player` access for one player profile and `team_professional` access for a team at the same time.

## Database Change

Migration: `20260512170318_AddMultipleUserSubscriptions`

- Removed the unique index on `Subscriptions.UserId`.
- Added non-unique index `IX_Subscriptions_UserId`.
- Added `ScopeType` with default `account`.
- Added nullable `ScopeId`.
- Added index `IX_Subscriptions_UserId_PlanType_Scope`.

## Database Update Status

The migration was generated and tests passed, but applying it to the configured PostgreSQL database is still pending.

Attempted `dotnet ef database update` on 2026-05-12:

- Sandbox run could not open the VPN socket.
- Escalated run reached `192.168.48.15:5432`.
- PostgreSQL rejected the configured `postgres` password with `28P01: password authentication failed`.

Update the server/local connection string with the correct database credentials, then rerun the EF database update before deploying code that reads `Subscriptions.ScopeType` or `Subscriptions.ScopeId`.

## Scope Types

- `account`: subscription applies broadly to the user account.
- `player`: subscription applies to one player profile.
- `team`: subscription applies to one team.
- `organization`: subscription applies to one organization.

Scope ownership validation should be added when player, team, and organization management APIs are mature enough to enforce it.
