# Account Security API 2026-05-01

## Implemented

Added custom account security endpoints under `/api/account`:

- `POST /api/account/login`
- `POST /api/account/refresh-token`
- `POST /api/account/logout`
- `GET /api/account/me`
- `POST /api/account/verify-email`
- `POST /api/account/resend-email-verification`
- `POST /api/account/send-phone-verification`
- `POST /api/account/verify-phone`

All state-changing endpoints use `POST`. No `PUT`, `PATCH`, or `DELETE` verbs were added.

## Authentication

The API now issues JWT bearer access tokens and refresh tokens.

- Access tokens are short-lived.
- Refresh tokens are random, hashed at rest, stored in the existing ASP.NET Identity user token table, and rotated on refresh.
- Logout revokes refresh tokens. Existing access tokens expire naturally.
- `GET /api/account/me` verifies bearer token authentication.

## Verification

Email verification:

- Registration sends an email confirmation token through `IAccountEmailSender`.
- Login requires confirmed email.
- Resend verification uses a generic response to avoid account enumeration.

Phone verification:

- Authenticated users can request a phone verification code.
- Phone verification uses ASP.NET Core Identity change-phone-number tokens.
- The development sender logs codes. A real SMS provider must replace it before production.

## Brute-Force Protection

- ASP.NET Core Identity lockout is enabled for failed password attempts.
- Accounts lock for 15 minutes after five failed login attempts.
- Account endpoints are protected by ASP.NET Core rate limiting.
- The current account security rate limit is 20 requests per minute per remote address and endpoint path.

## Configuration

JWT settings are configured under `Jwt`:

- `Issuer`
- `Audience`
- `SigningKey`
- `AccessTokenMinutes`
- `RefreshTokenDays`

Do not use the placeholder signing key in production. Set the production signing key through environment variables or a secret store.
