# User Management API

Date: 2026-05-01

## Scope

Added platform administrator account management endpoints under `/api/user-management`.

The API supports:

- Paginated user list/search
- User detail lookup
- Admin user creation
- Profile updates
- Additive account type combinations through Identity roles
- Email and phone verification state changes
- Lock/unlock
- Soft deactivate/reactivate
- Admin-triggered password reset email

## Authorization

All user management endpoints require `PlatformAdmin`.

Self-protection rules:

- A platform administrator cannot remove their own `PlatformAdmin` account type.
- A platform administrator cannot lock their own account.
- A platform administrator cannot deactivate their own account.

## HTTP Verb Rule

All state-changing actions use `POST`; read-only actions use `GET`.

No `PUT`, `PATCH`, or `DELETE` endpoints were added.

## Testing

Integration tests cover authentication requirement, `PlatformAdmin` authorization, admin create, filtered list, account type replacement, deactivate/reactivate, and self-protection for `PlatformAdmin`.
