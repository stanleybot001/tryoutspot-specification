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

Creates a new active user account using ASP.NET Core Identity. Account types are additive, so one user can register as multiple types.

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

Current implementation note: reset tokens are generated with ASP.NET Core Identity and sent through `LoggingAccountEmailSender`. Replace that sender with real email/SMS delivery before production.

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
