# User Access Subscription Scenario Review - 2026-05-12

## Purpose

Dry-run the current user access and subscription code paths without creating live users or subscriptions.

## Registration Paths

### Email/password registration

- `POST /api/account/register` and `POST /account/register` create an active user.
- Required fields: email, password, first name, last name, and at least one public account type.
- Supported public account types: `Parent`, `Player`, `Coach`, `TeamManager`, `AcademyDirector`, `OrganizationAdmin`.
- `PlatformAdmin` cannot be self-selected.
- Duplicate account types are ignored after the first normalized match.
- SMS consent is optional. If selected, a phone number is required.
- Email confirmation token is sent after registration.
- Password login requires confirmed email.

### Social registration

- `GET /api/social-login/challenge/{provider}` starts provider login.
- `GET /api/social-login/callback` returns tokens for already linked users.
- Unlinked social identities return an external login token and available account types.
- `POST /api/social-login/register` creates a passwordless user linked to the provider.
- Existing email addresses must sign in and link the social login instead of creating a duplicate account.
- Email is marked confirmed only when the provider returns verified email.
- Social users can receive tokens immediately, but confirmed-email-protected API endpoints still require `EmailConfirmed`.

## Account Type Combination Groups

The six public account types produce 63 possible non-empty combinations. Current onboarding recommendations collapse into these behavior groups:

| Recommended plans | Combination count | Account type pattern |
| --- | ---: | --- |
| `free_player_parent`, `premium_player` | 3 | Parent and/or Player only |
| `team_basic`, `team_professional` | 3 | Coach and/or TeamManager only |
| `enterprise_organization` | 1 | OrganizationAdmin only |
| `team_basic`, `team_professional`, `enterprise_organization` | 11 | AcademyDirector with or without Coach/TeamManager/OrganizationAdmin; Coach/TeamManager combined with OrganizationAdmin |
| `free_player_parent`, `premium_player`, `enterprise_organization` | 3 | Parent/Player combined with OrganizationAdmin only |
| `free_player_parent`, `premium_player`, `team_basic`, `team_professional` | 9 | Parent/Player combined with Coach and/or TeamManager |
| `free_player_parent`, `premium_player`, `team_basic`, `team_professional`, `enterprise_organization` | 33 | Parent/Player combined with AcademyDirector or with both team and organization roles |

## Subscription Plans

| Plan code | Stripe required | Billing intervals | Grants paid entitlement when status is |
| --- | --- | --- | --- |
| `free_player_parent` | No | Monthly display only, amount `$0` | Not subscription-gated |
| `premium_player` | Yes | Monthly, annual | `active`, `trialing` |
| `team_basic` | Yes | Monthly only | `active`, `trialing` |
| `team_professional` | Yes | Monthly, annual | `active`, `trialing` |
| `enterprise_organization` | Yes | Monthly, annual | `active`, `trialing` |

## Entitlement Simulation

- Free entitlements come only from `Parent` or `Player` roles.
- Team and organization roles do not grant free team features by themselves.
- Paid entitlements come from every active or trialing `Subscription` row linked to the user.
- Paid features are granted only for `active` or `trialing` subscription statuses.
- `checkout_started`, `canceled`, `past_due`, `unpaid`, and failed/unknown statuses do not grant paid features.
- Entitlements are the union of free role features plus active/trialing plan features.
- Multiple paid subscriptions can contribute features at the same time.

## Billing Flow Simulation

1. User must be authenticated and email-confirmed for checkout.
2. `POST /api/billing/checkout-session` validates plan code and billing interval.
3. Free plan is rejected for Stripe checkout because it does not require Stripe.
4. The requested paid plan must be eligible for at least one of the user's public account types.
5. Annual interval is rejected for `team_basic`.
6. Optional subscription scope must match the selected plan:
   - `premium_player`: `account` or `player`
   - `team_basic`, `team_professional`: `account` or `team`
   - `enterprise_organization`: `account` or `organization`
7. Stripe price ID must be configured for the selected plan and interval.
8. Checkout creates or reuses a Stripe customer.
9. Local subscription is recorded as `checkout_started`, but no paid features are granted yet.
10. Stripe webhook sync changes the local subscription to `active` or `trialing`.
11. Entitlement service begins granting plan features.
12. Stripe cancellation or non-entitling status removes paid features.

## Plan Eligibility Decision

- Plan eligibility is additive. A mixed-role user sees and can request the union of plans for all of their account types.
- `Parent` or `Player` can use `free_player_parent` and `premium_player`.
- `Coach`, `TeamManager`, or `AcademyDirector` can use `team_basic` and `team_professional`.
- `OrganizationAdmin` or `AcademyDirector` can use `enterprise_organization`.
- A `Parent + Coach` user can choose a player/parent plan or a team plan.
- A `Player + Coach` user can choose a player plan or a team plan.

## Current Gaps To Decide

- Changing account types does not check whether the existing subscription still matches the new role mix.
- `OrganizationAdmin` alone is recommended only Enterprise; that matches current code, but we should confirm whether organization admins should also see Team Basic/Professional.
- Webhook handling currently relies on subscription events and checkout completion. Invoice events are documented as desirable later but are not currently handled.
- Scope ids are recorded but not fully validated against player/team/organization ownership yet. Add that validation when those management APIs are built.

## Recommended Next Verification Tests

- Checkout rejects unsupported billing interval for `team_basic` annual.
- Checkout rejects free plan.
- Checkout rejects role/plan mismatches for single-role users.
- Checkout permits mixed-role users to request any plan eligible for one of their roles.
- Checkout permits multiple same-plan subscriptions with different scopes.
- Checkout rejects duplicate active subscriptions for the same plan and scope.
- Entitlements for all five plan codes and non-entitling statuses.
- Entitlements merge active multiple subscriptions.
- Role changes preserve platform admin role and update public roles only.
- Mixed-role user behavior for `Parent + Coach`, `Player + Coach`, and `AcademyDirector + Parent`.
