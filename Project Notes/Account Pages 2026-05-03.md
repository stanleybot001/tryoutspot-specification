# Account Pages - 2026-05-03

## Decision

Add complete server-rendered MVC pages for the account flow while keeping signup short.

The pages use normal GET and POST form submissions. They do not use AJAX, PUT, PATCH, or DELETE.

All page POST actions use ASP.NET Core anti-forgery validation.

## Pages Added

- `GET /account/register`
- `POST /account/register`
- `GET /account/register-confirmation`
- `GET /account/login`
- `POST /account/login`
- `POST /account/logout`
- `GET /account/forgot-password`
- `POST /account/forgot-password`
- `GET /account/forgot-password-confirmation`
- `GET /account/reset-password`
- `POST /account/reset-password`
- `GET /account/verify-email`
- `GET /account/external-login`
- `GET /account/external-callback`
- `GET /account/complete-social-registration`
- `POST /account/complete-social-registration`
- `POST /account/link-social-login`
- `GET /account/onboarding`
- `POST /account/onboarding/account-types`

## Signup Flow

The required signup fields are:

- Email address
- Password and password confirmation
- First name
- Last name
- One or more account types

Optional fields are included on the same page:

- Phone number
- Transactional SMS consent checkbox
- Date of birth
- ZIP code
- City
- State

This keeps the first signup decision simple while still allowing users to provide useful profile details immediately.

The SMS consent checkbox is optional and unchecked by default. If selected, the user must also enter a phone number. The exact consent text is stored with the user record for auditability.

## Account Type Direction

Account types remain additive. A user can select multiple roles, such as `Parent` and `Coach` or `Player` and `Coach`.

Plan selection remains separate from account type. The onboarding page shows recommended plans but does not require paid plan selection before the user can continue.

## Verification

Added automated tests for:

- Complete register form rendering
- Login, forgot password, and reset password pages
- Server-rendered registration creating users and roles
- SMS consent audit capture during page registration
- Cookie-backed onboarding access after login
