# TryOutSpot Subscription Permission Matrix

Updated: May 14, 2026

This matrix maps logged-in access to current subscription entitlements and shows what is already enforced vs what still needs implementation.

---

## 1) Current authorization building blocks (in code today)

- Authentication:
  - Web cookie auth for web pages.
  - JWT bearer auth for API endpoints.
- Base policies:
  - `TryOutSpot.ActiveUser`
  - `TryOutSpot.ConfirmedEmail`
- Feature policies:
  - Dynamic policy prefix: `TryOutSpot.Feature:{featureCode}`
  - Requirement handlers:
    - `FeatureAccessRequirement` (single feature)
    - `AnyFeatureAccessRequirement` (any of N features)
- Current feature-based web policies:
  - `TryOutSpot.ManagePlayerProfile` -> `players.profiles.basic`
  - `TryOutSpot.ManageTeamProfile` -> any of:
    - `opportunities.post.limited`
    - `opportunities.post.unlimited`

---

## 2) Logged-in route matrix (current enforcement)

### Web routes (`/account/...`)

| Route | Current gate | Subscription-aware check | Status |
|---|---|---|---|
| `GET /account/onboarding` | Authenticated cookie | No direct feature gate | Implemented |
| `POST /account/onboarding/account-types` | Authenticated cookie | No direct feature gate | Implemented |
| `GET /account/onboarding/add-player-profile` | `ManagePlayerProfile` policy | Requires `players.profiles.basic` | Implemented |
| `POST /account/onboarding/add-player-profile` | `ManagePlayerProfile` policy | Requires `players.profiles.basic` | Implemented |
| `GET /account/onboarding/add-team-or-organization` | `ManageTeamProfile` policy | Requires `opportunities.post.limited` OR `opportunities.post.unlimited` | Implemented |
| `POST /account/onboarding/add-team-or-organization` | `ManageTeamProfile` policy | Requires `opportunities.post.limited` OR `opportunities.post.unlimited` | Implemented |
| `GET /account/onboarding/choose-plan` | Authenticated cookie | Plan options filtered by account type roles | Implemented |
| `POST /account/onboarding/choose-plan` | Authenticated cookie | Plan eligibility + interval rules + Stripe config checks | Implemented |
| `GET /account/settings` | Authenticated cookie | Membership summary built from subscription rows | Implemented |
| `POST /account/settings/open-billing-portal` | Authenticated cookie | Requires Stripe configured + linked customer | Implemented |
| `POST /account/settings/cancel-membership` | Authenticated cookie | Schedules Stripe period-end cancellation for active paid plans | Implemented |

### Billing API routes (`/api/billing/...`)

| Route | Current gate | Subscription-aware check | Status |
|---|---|---|---|
| `GET /api/billing/me` | `ConfirmedEmail` | Returns current subscription state and entitlement-active flags | Implemented |
| `POST /api/billing/checkout-session` | `ConfirmedEmail` | Validates plan eligibility, scope, interval, Stripe price mapping | Implemented |
| `POST /api/billing/customer-portal-session` | `ConfirmedEmail` | Requires linked Stripe customer | Implemented |
| `POST /api/billing/stripe/webhook` | Signature validation | Syncs Stripe subscription state into local entitlements | Implemented |

### Onboarding API routes (`/api/onboarding/...`)

| Route | Current gate | Subscription-aware check | Status |
|---|---|---|---|
| `GET /api/onboarding/status` | `[Authorize]` | Steps computed using subscription + entitlements | Implemented |
| `POST /api/onboarding/account-types` | `[Authorize]` | Roles update; downstream plan eligibility updates | Implemented |

---

## 3) Current subscription -> permission outcomes (as implemented)

| Plan / state | Effective access outcome |
|---|---|
| Free Player/Parent (active) | Can access player profile onboarding (`players.profiles.basic`) and other free player/parent features. |
| Premium Player (active/trialing) | Free access plus premium player feature codes in entitlement set. |
| Team Basic (active/trialing) | Can access team profile onboarding (`opportunities.post.limited` present). |
| Free Coach (internal plan) | Grants starter team access with limited posting entitlement (`opportunities.post.limited`) under free-tier quota rules. |
| Team Professional (active/trialing) | Team pro entitlements granted; annual-commitment plan behavior enforced in catalog and Stripe config. |
| Enterprise Organization (active/trialing) | Enterprise entitlements granted; annual-commitment plan behavior enforced in catalog and Stripe config. |
| Paid plan cancel-at-period-end | Entitlements remain until period end; cancellation state reflected in subscription row/web summary. |
| Team Pro/Enterprise non-entitling or cancel-at-period-end transitions | Team and organization records soft-deactivated by sync service lifecycle rules. |

---

## 4) Recommended next enforcement matrix (what to implement next)

These are the feature-to-capability checks to add as each module is built.

| Capability | Required feature(s) | Enforcement pattern |
|---|---|---|
| Post opportunity (team) | `opportunities.post.limited` OR `opportunities.post.unlimited` | Policy on create/publish actions + monthly quota logic for limited plan |
| Limited posting quota (Team Basic) | `opportunities.post.limited` | Service-level monthly usage check (hard stop at quota) |
| Unlimited posting (Team Pro/Enterprise) | `opportunities.post.unlimited` | Skip quota check when this feature is present |
| Player search (basic) | `players.search.basic` | Policy or service guard on search endpoint |
| Player search (advanced filters) | `players.search.advanced` | Service guard for advanced filter fields |
| Team direct messaging from player side | `communication.team.direct` | Policy on message-create endpoints |
| Team bulk messaging | `communication.bulk` | Policy on bulk send endpoints |
| Player analytics views | `registrations.analytics.player` | Policy on analytics API/view routes |
| Team analytics basic | `analytics.team.basic` | Policy on team analytics endpoints |
| Team analytics detailed | `analytics.team.detailed` | Policy on detailed analytics endpoints |
| Priority review | `registrations.priority_review` | Service logic on application ranking/flagging |
| Early access opportunities | `opportunities.early_access` | Service query filter by release window |
| Premium registration management | `registrations.manage.premium` | Policy + service rules on advanced applicant workflows |
| Multi-team admin operations | `teams.manage.multiple` | Policy on organization/team management routes |
| API integration endpoints | `api.access` | Policy for API key creation/use and integration endpoints |
| White-label/branding advanced | `branding.custom`, `branding.white_label` | Policy on branding config endpoints |
| Advanced security controls | `security.advanced` | Policy on enterprise security settings endpoints |

---

## 5) Notes before implementation

- Entitlements currently derive from:
  1) free features by role, and
  2) active/trialing subscriptions.
- Dynamic feature policy support already exists, so most new gates can be added with:
  - `[Authorize(Policy = TryOutSpotAuthorizationPolicies.Feature("feature.code"))]`
  - or a specific named policy when multi-feature rules are needed.
- For quota and timed-release behavior (posting limits, early access windows), enforce in application services, not only controller attributes.
