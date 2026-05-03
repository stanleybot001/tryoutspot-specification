# Onboarding API - 2026-05-03

## Decision

Keep signup short and defer setup details.

Initial signup should ask only for:

- Sign-in method: email/password or configured social login
- Basic name/email information
- One or more account types

Do not force player profile details, team setup, organization verification, or paid plan selection during initial account creation.

## Flow

1. User creates account with email/password or social login.
2. User chooses one or more account types.
3. API returns tokens and onboarding status.
4. Dashboard shows required steps first and optional setup prompts second.
5. Paid plan selection is skippable until the user tries to use paid features.

## API Added

- `GET /api/onboarding/options`
- `GET /api/onboarding/status`
- `POST /api/onboarding/account-types`

The onboarding status contract separates:

- Required steps: account types and email verification
- Optional steps: player profile, team/organization setup, and paid plan selection
- Free feature codes already available to the user
- Recommended plans based on account type

## Notes

Account types remain additive. A user can be `Parent` and `Coach`, `Player` and `Coach`, or any supported public combination.

Plan/tier is separate from account type. Users can start free and upgrade later.
