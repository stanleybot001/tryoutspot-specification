# TryOutSpot API Reference

This file tracks the human-facing API contract as endpoints are added. The generated OpenAPI document is available in development at:

- `/openapi/v1.json`
- `/swagger`

State-changing endpoints use `POST`; read-only endpoints use `GET`. Do not add `PUT`, `PATCH`, or `DELETE` unless explicitly approved.

## Account

### `GET /api/account/account-types`

Lists public account types available during registration.

Response:

```json
[
  { "name": "Parent" },
  { "name": "Player" },
  { "name": "Coach" },
  { "name": "TeamManager" },
  { "name": "AcademyDirector" },
  { "name": "OrganizationAdmin" }
]
```

### `POST /api/account/register`

Creates a new active user account using ASP.NET Core Identity. Account types are additive, so one user can register as multiple types. Registration sends an email verification token through the configured account email sender.

Request:

```json
{
  "email": "coach.parent@example.com",
  "password": "Tryout2026",
  "firstName": "Taylor",
  "lastName": "Morgan",
  "phoneNumber": "555-555-1234",
  "dateOfBirth": null,
  "zipCode": "73102",
  "city": "Oklahoma City",
  "state": "OK",
  "accountTypes": ["Parent", "Coach"]
}
```

Success response: `201 Created`

```json
{
  "userId": "00000000-0000-0000-0000-000000000000",
  "email": "coach.parent@example.com",
  "firstName": "Taylor",
  "lastName": "Morgan",
  "accountTypes": ["Parent", "Coach"],
  "isActive": true
}
```

Validation response: `400 Bad Request`

### `POST /api/account/login`

Authenticates an active email-verified account and returns a bearer access token plus refresh token.

Request:

```json
{
  "email": "coach.parent@example.com",
  "password": "Tryout2026"
}
```

Success response: `200 OK`

```json
{
  "tokenType": "Bearer",
  "accessToken": "jwt-access-token",
  "accessTokenExpiresAtUtc": "2026-05-01T20:15:00Z",
  "refreshToken": "refresh-token",
  "refreshTokenExpiresAtUtc": "2026-05-31T20:00:00Z",
  "user": {
    "userId": "00000000-0000-0000-0000-000000000000",
    "email": "coach.parent@example.com",
    "firstName": "Taylor",
    "lastName": "Morgan",
    "accountTypes": ["Parent", "Coach"],
    "isActive": true
  }
}
```

Failure responses:

- `400 Bad Request` when email verification is still required
- `401 Unauthorized` for invalid credentials
- `423 Locked` after repeated failed login attempts

### `POST /api/account/refresh-token`

Rotates a valid refresh token and returns a new bearer token pair.

Request:

```json
{
  "refreshToken": "refresh-token"
}
```

Success response: `200 OK`

Failure response: `401 Unauthorized`

### `POST /api/account/logout`

Revokes a refresh token. Existing bearer access tokens expire naturally.

Request:

```json
{
  "refreshToken": "refresh-token"
}
```

Success response: `200 OK`

### `GET /api/account/me`

Returns the authenticated account and verification state. Requires `Authorization: Bearer <token>`.

Success response: `200 OK`

```json
{
  "userId": "00000000-0000-0000-0000-000000000000",
  "email": "coach.parent@example.com",
  "firstName": "Taylor",
  "lastName": "Morgan",
  "accountTypes": ["Parent", "Coach"],
  "isActive": true,
  "emailConfirmed": true,
  "phoneNumber": "555-555-1234",
  "phoneNumberConfirmed": false
}
```

Failure response: `401 Unauthorized`

### `POST /api/account/verify-email`

Verifies an account email address using the email confirmation token.

Request:

```json
{
  "email": "coach.parent@example.com",
  "token": "identity-email-confirmation-token"
}
```

Success response: `200 OK`

Failure response: `400 Bad Request`

### `POST /api/account/resend-email-verification`

Resends email verification instructions for an active unverified account. The response is generic so the endpoint does not reveal whether an email address exists.

Request:

```json
{
  "email": "coach.parent@example.com"
}
```

Success response: `200 OK`

### `POST /api/account/forgot-password`

Starts the forgot-password flow for an active account. The response is intentionally generic so the endpoint does not reveal whether an email address exists.

Request:

```json
{
  "email": "coach.parent@example.com"
}
```

Success response: `200 OK`

```json
{
  "message": "If an active account exists for that email address, password reset instructions will be sent."
}
```

Delivery note: reset tokens are generated with ASP.NET Core Identity and sent through the configured account email sender. Development can use either logging delivery or Resend.

### `POST /api/account/reset-password`

Completes password reset using an ASP.NET Core Identity reset token.

Request:

```json
{
  "email": "coach.parent@example.com",
  "token": "identity-reset-token",
  "newPassword": "NewTryout2026"
}
```

Success response: `200 OK`

```json
{
  "message": "The password has been reset."
}
```

Validation response: `400 Bad Request`

### `POST /api/account/delete-account`

Soft deletes an active account after password verification. The user row is retained, `IsActive` is set to `false`, the account is locked, and the security stamp is refreshed.

Request:

```json
{
  "email": "coach.parent@example.com",
  "password": "Tryout2026",
  "reason": "Optional reason"
}
```

Success response: `200 OK`

```json
{
  "message": "The account has been deleted."
}
```

Failure response: `400 Bad Request`

### `POST /api/account/send-phone-verification`

Sends a phone verification code for the authenticated account. Requires `Authorization: Bearer <token>`.

Request:

```json
{
  "phoneNumber": "555-555-1234"
}
```

Success response: `200 OK`

Failure responses:

- `400 Bad Request`
- `401 Unauthorized`

Delivery note: phone verification codes are sent through the configured account SMS sender. Development can use either logging delivery or Twilio.

### `POST /api/account/verify-phone`

Verifies the authenticated account phone number using the delivered code. Requires `Authorization: Bearer <token>`.

Request:

```json
{
  "phoneNumber": "555-555-1234",
  "code": "123456"
}
```

Success response: `200 OK`

Failure responses:

- `400 Bad Request`
- `401 Unauthorized`

## Account Security

Account endpoints use ASP.NET Core rate limiting and return `429 Too Many Requests` when the current account security policy is exceeded.

Login also uses ASP.NET Core Identity lockout. Repeated failed login attempts temporarily lock the account and return `423 Locked`.

## Billing

### `GET /api/billing/plans`

Lists the public billing plan catalog and the feature codes included in each plan.

Response:

```json
[
  {
    "code": "premium_player",
    "name": "Premium Player",
    "audience": "Player/Parent",
    "description": "Paid player profile, discovery, messaging, and analytics features.",
    "monthlyAmount": 9.99,
    "annualAmount": 99,
    "currency": "USD",
    "trialDays": null,
    "requiresStripeSubscription": true,
    "includedFeatureCodes": [
      "opportunities.browse",
      "players.profiles.basic",
      "opportunities.apply"
    ]
  }
]
```

### `GET /api/billing/features`

Lists all known feature codes used by entitlement checks.

Response:

```json
[
  {
    "code": "opportunities.browse",
    "name": "Browse opportunities",
    "description": "Search and view public tryouts, tournaments, camps, and roster openings.",
    "isPaidFeature": false
  }
]
```
