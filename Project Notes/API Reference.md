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

## User Management

User management endpoints require a bearer token for a user in the `PlatformAdmin` account type. State-changing user management actions use `POST`.

### `GET /api/user-management/users`

Lists users with optional filters.

Query parameters:

- `search`: optional email/name search
- `accountType`: optional role filter such as `Parent`, `Coach`, or `PlatformAdmin`
- `isActive`: optional `true` or `false`
- `page`: defaults to `1`
- `pageSize`: defaults to `25`, maximum `100`

Success response: `200 OK`

```json
{
  "users": [
    {
      "userId": "00000000-0000-0000-0000-000000000000",
      "email": "coach.parent@example.com",
      "firstName": "Taylor",
      "lastName": "Morgan",
      "accountTypes": ["Coach", "Parent"],
      "isActive": true,
      "emailConfirmed": true,
      "phoneNumberConfirmed": false,
      "createdAtUtc": "2026-05-01T20:00:00Z",
      "updatedAtUtc": "2026-05-01T20:00:00Z"
    }
  ],
  "page": 1,
  "pageSize": 25,
  "totalCount": 1,
  "totalPages": 1
}
```

### `GET /api/user-management/users/{userId}`

Returns administrative account detail for one user.

Success response: `200 OK`

Failure responses:

- `401 Unauthorized`
- `403 Forbidden`
- `404 Not Found`

### `POST /api/user-management/users`

Creates a managed user account. Account types are additive and can include admin-only roles.

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
  "accountTypes": ["Parent", "Coach"],
  "emailConfirmed": true,
  "phoneNumberConfirmed": false
}
```

Success response: `201 Created`

### `POST /api/user-management/users/{userId}/profile`

Updates editable user profile fields.

### `POST /api/user-management/users/{userId}/account-types`

Replaces a user's account type roles. The API prevents a platform administrator from removing their own `PlatformAdmin` role.

Request:

```json
{
  "accountTypes": ["Coach", "TeamManager"]
}
```

### `POST /api/user-management/users/{userId}/verification`

Sets email and phone verification flags.

Request:

```json
{
  "emailConfirmed": true,
  "phoneNumberConfirmed": true
}
```

### `POST /api/user-management/users/{userId}/lock`

Locks a user account until it is manually unlocked. The API prevents a platform administrator from locking their own account.

### `POST /api/user-management/users/{userId}/unlock`

Unlocks a user account and clears failed access count.

### `POST /api/user-management/users/{userId}/deactivate`

Soft deactivates a user account, locks it, and refreshes the security stamp so existing bearer tokens stop working.

### `POST /api/user-management/users/{userId}/reactivate`

Reactivates a soft-deactivated account and clears lockout state.

### `POST /api/user-management/users/{userId}/send-password-reset`

Sends password reset instructions for an active account through the configured account email sender.

## Social Login

Social login uses ASP.NET Core external authentication plus ASP.NET Core Identity external logins. Google and Facebook handlers are wired when their credentials are configured. Apple is listed as a planned provider and remains disabled until Apple Developer credentials and token validation are configured.

### `GET /api/social-login/providers`

Lists supported social login providers and whether each provider is configured.

Success response: `200 OK`

```json
[
  {
    "provider": "Google",
    "displayName": "Google",
    "isConfigured": true,
    "challengeUrl": "/api/social-login/challenge/Google"
  },
  {
    "provider": "Facebook",
    "displayName": "Facebook",
    "isConfigured": false,
    "challengeUrl": null
  },
  {
    "provider": "Apple",
    "displayName": "Apple",
    "isConfigured": false,
    "challengeUrl": null
  }
]
```

### `GET /api/social-login/challenge/{provider}`

Starts an external login challenge for a configured provider. Supported provider values are `Google`, `Facebook`, and eventually `Apple`.

Success response: `302 Found`

Failure response: `400 Bad Request`

### `GET /api/social-login/callback`

Completes the external provider callback.

If the social login is already linked to an active account, the API returns an `AuthTokenResponse`.

If the social login is new, the API returns `409 Conflict` with a short-lived `externalLoginToken`. The client can use that token to complete registration or link the social login to an authenticated account.

### `POST /api/social-login/register`

Creates a new external-only account from an external login token.

Request:

```json
{
  "externalLoginToken": "short-lived-token",
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

Success response: `201 Created` with an `AuthTokenResponse`.

Failure response: `400 Bad Request`

### `GET /api/social-login/linked`

Lists social logins linked to the authenticated account. Requires `Authorization: Bearer <token>`.

### `POST /api/social-login/link`

Links a social login to the authenticated account using an external login token.

Request:

```json
{
  "externalLoginToken": "short-lived-token"
}
```

### `POST /api/social-login/unlink`

Unlinks a social login from the authenticated account. The API prevents removing the only sign-in method from an external-only account.

Request:

```json
{
  "provider": "Google"
}
```

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
