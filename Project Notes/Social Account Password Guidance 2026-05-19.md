# Social Account Password Guidance

Date: 2026-05-19

## Context

Users who create a TryOutSpot account through Google or Facebook may not have a local TryOutSpot password. If they later try the email/password form, the old behavior returned a generic invalid login message even though the email existed and the intended sign-in path was social login.

## Decision

For web login and registration pages, TryOutSpot now detects active accounts that have no local password and have one or more linked social providers:

- Email/password login shows a provider-specific message before attempting password sign-in.
- Registration with an existing social-only email shows a provider-specific message instead of a duplicate-account dead end.
- Forgot password copy explains that password reset can also add an email/password sign-in to an account created with Google or Facebook.

This keeps one account per email while still letting users add a local password later through the existing reset-password flow.

## Security Notes

TryOutSpot does not automatically link a new social provider to an existing account by matching email. Users must sign in first and explicitly link another provider from the authenticated account flow.
