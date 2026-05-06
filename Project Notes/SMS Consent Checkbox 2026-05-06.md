# SMS Consent Checkbox - 2026-05-06

## Purpose

TryOutSpot needs explicit transactional SMS consent before sending Twilio account messages such as phone verification codes, security notices, password reset notifications, and tryout registration updates.

## User Experience

- Registration and social-registration pages include an unchecked SMS consent checkbox.
- SMS consent is optional and is not required to create an account.
- If a user checks the consent box, a phone number is required.
- The checkbox text is centralized in `TryOutSpotSmsConsent.CheckboxText` so page text, API behavior, and audit storage stay consistent.

## Audit Fields

The `Users` table stores:

- `SmsConsentAccepted`
- `SmsConsentAcceptedAt`
- `SmsConsentSource`
- `SmsConsentText`

## API Behavior

- `POST /api/account/register` and `POST /api/social-login/register` accept `smsConsentAccepted`.
- `POST /api/account/send-phone-verification` refuses to send SMS when `SmsConsentAccepted` is false.
- The current user/onboarding responses expose SMS consent status.

## Migration

Migration `20260506033039_AddSmsConsentAudit` adds the audit columns. Apply it before deploying this code to a database that does not already have these columns.
