# Account API 2026-05-01

## Identity Approach

The account API uses ASP.NET Core Identity with the existing `Users` table as the Identity user table.

`User` now inherits from `IdentityUser<Guid>` while preserving the existing TryOutSpot profile fields and navigation properties.

Global account capabilities are represented as ASP.NET Identity roles so one person can hold multiple account types at the same time. Team-specific responsibilities still belong in the existing domain table `UserTeamRoles`.

## Seeded Account Roles

- `Parent`
- `Player`
- `Coach`
- `TeamManager`
- `AcademyDirector`
- `OrganizationAdmin`
- `PlatformAdmin`

Public registration can request all roles except `PlatformAdmin`.

## API Endpoints

- `POST /api/account/register`
- `POST /api/account/forgot-password`
- `POST /api/account/reset-password`
- `POST /api/account/delete-account`
- `GET /api/account/account-types`

No `PUT`, `PATCH`, or `DELETE` verbs are used.

## Password Reset

Password reset currently uses ASP.NET Core Identity reset tokens and a logging sender. A real email/SMS sender must replace `LoggingAccountEmailSender` before production use.

## Soft Delete

`delete-account` verifies the email/password, sets `Users.IsActive = false`, locks the account, and updates the security stamp. It does not hard-delete user records.

## Database Migration

Migration applied to `tryoutspot_prod`:

- `20260501153246_AddIdentityAccountSupport`

The migration added ASP.NET Identity role/claim/login/token tables, Identity columns on `Users`, seeded roles, and backfilled the existing user row with normalized Identity fields.
