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

The migration was applied to `tryoutspot_prod` on 2026-05-12 using the `Production` environment connection string from `appsettings.Production.json`.

Earlier attempts with the default appsettings connection failed because `appsettings.json` still contains the placeholder `Password=CHANGE_ME`. Use the Production configuration when applying migrations against the live PostgreSQL database.

## Scope Types

- `account`: subscription applies broadly to the user account.
- `player`: subscription applies to one player profile.
- `team`: subscription applies to one team.
- `organization`: subscription applies to one organization.

Scope ownership validation should be added when player, team, and organization management APIs are mature enough to enforce it.
