# User Access System - 2026-05-10

## Completed

- Added a signed-in account settings page at `/account/settings`.
- Added normal MVC POST forms for profile, email change, phone update, phone verification, password set/change, account type selection, and SMS consent.
- Kept all state-changing web account actions on `POST`; no `PUT`, `PATCH`, `DELETE`, AJAX, or client-only account changes were introduced.
- Email changes now send a confirmation link to the new email address before the account email is updated.
- Phone verification uses the same Identity phone token and SMS sender abstraction as the API.
- Password settings support both local-password users and social-login users who need to add a password.
- SMS consent can be opted in or out from account settings and records the account-settings source.
- Added reusable authorization policies for active users, confirmed email, and feature-code entitlement checks.

## Authorization Direction

- Use `TryOutSpotAuthorizationPolicies.ActiveUser` for signed-in features that require an active account.
- Use `TryOutSpotAuthorizationPolicies.ConfirmedEmail` for sensitive features that should require verified email.
- Use `TryOutSpotAuthorizationPolicies.Feature("<feature-code>")` for paid/free feature gating once Stripe membership state is wired into subscriptions.
- Feature checks are backed by `IEntitlementService`, which combines public account roles, plan codes, subscription status, and billing feature codes.

## Tested

- Account settings page requires a web cookie and renders all management forms.
- Profile, account type, and SMS consent POST flows update the current user.
- Email change, phone verification, and password change flows update the current user while keeping the session valid.
- Feature authorization policy grants free features and denies paid features until an active paid subscription exists.
- Full test suite passed: `56` tests.

## Next

- Build the Stripe membership system.
- Map Stripe customers/subscriptions to the existing `Subscription` entity.
- Add checkout/session creation endpoints.
- Add webhook processing for subscription status changes.
- Mark user accounts with the correct plan and status so `IEntitlementService` can gate role-specific paid features.
