# SMS Consent Audit Migration - 2026-05-06

## Migration

`20260506033039_AddSmsConsentAudit`

## Purpose

Adds audit fields to `Users` so TryOutSpot can prove transactional SMS consent before sending Twilio messages.

## Columns

- `SmsConsentAccepted` boolean, default `false`
- `SmsConsentAcceptedAt` timestamp with time zone, nullable
- `SmsConsentSource` varchar(100), nullable
- `SmsConsentText` varchar(1000), nullable

## SQL Script

The generated SQL script is:

- `db/20260506033039_AddSmsConsentAudit.sql`

Apply this migration before deploying code that expects these columns.

## Applied

Applied to `tryoutspot_prod` on 2026-05-06 with `dotnet ef database update`.
