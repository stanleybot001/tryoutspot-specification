# TryOutSpot Team Bundle Canonical Feature Matrix

Updated: May 28, 2026
Status: Proposed canonical source for Team bundle entitlements

## Purpose

This document defines the Team bundle feature ladder in implementation-ready terms:

- stable `feature_code` keys for policy/entitlement checks
- explicit `limit_type` for enforcement
- exact values by tier

This matrix is intended to be the single source of truth for:

- pricing/plan marketing copy
- checkout and plan eligibility
- entitlement resolution
- API and web authorization policies

## Team Bundle Tiers

1. `free_coach`
2. `team_basic`
3. `team_professional`
4. `enterprise_organization`

## Limit Type Definitions

- `boolean`: feature is either enabled or disabled
- `count_window`: numeric quota within a rolling window
- `count_max`: hard cap
- `enum`: discrete access level

---

## Canonical Feature Matrix

| feature_code | area | description | limit_type | free_coach | team_basic | team_professional | enterprise_organization |
|---|---|---|---|---:|---:|---:|---:|
| `teams.count.max` | Team Management | Maximum teams the account can actively manage. | `count_max` | 1 | 1 | 12 | 24 |
| `opportunities.tryout.post` | Tryout Listings | Permission to publish tryout listings. | `boolean` | true | true | true | true |
| `opportunities.tryout.post.quota` | Tryout Listings | Tryout listing publish quota in rolling window. | `count_window` | 1 / 6 months | 9 / 12 months | 24 / 12 months | 50 / 12 months |
| `opportunities.share.links` | Tryout Listings | Copy public listing links and ready-to-share post text for active opportunity listings. | `boolean` | true | true | true | true |
| `opportunities.flyers` | Tryout Listings | Link or upload a PDF or image event flyer, save it with the opportunity, and display it on the public listing. | `boolean` | true | true | true | true |
| `players.search.access` | Player Search | Player database search access level. | `enum` | preview | basic | advanced | advanced |
| `players.search.monthly_quota` | Player Search | Player search query quota per 30-day window. | `count_window` | 25 / 30 days | 300 / 30 days | 3000 / 30 days | 10000 / 30 days |
| `registrations.tryout.manage.standard` | Tryout Registration | Standard registration workflow access (forms, applicant list, status updates). | `boolean` | false | true | true | true |
| `registrations.tryout.manage.premium` | Tryout Registration | Premium registration workflow access (advanced filters, bulk actions, scoring tools). | `boolean` | false | false | true | true |
| `events.tournament.advertise` | Tournament Advertising | Ability to post tournament/event advertisements. | `boolean` | false | false | true | true |
| `communication.inapp.direct` | Communication | Direct in-app messaging with families/players. | `boolean` | false | true | true | true |
| `communication.email.bulk` | Communication | Bulk email campaigns and segmented sends. | `boolean` | false | true | true | true |
| `communication.sms.followers` | Communication | Short listing-update SMS messages to opted-in players and parents who follow a specific opportunity. | `boolean` | false | true | true | true |
| `communication.sms.bulk` | Communication | Bulk SMS campaigns through platform messaging service. | `boolean` | false | false | true | true |
| `analytics.team.basic` | Analytics | Basic team performance and listing activity metrics. | `boolean` | false | true | true | true |
| `analytics.team.detailed` | Analytics | Advanced reporting (conversion, cohort, source attribution). | `boolean` | false | false | true | true |
| `branding.custom` | Branding | Team-level branding customization. | `boolean` | false | false | true | true |
| `teams.manage.multiple` | Org Operations | Multi-team management console and cross-team controls. | `boolean` | false | false | true | true |
| `workflows.approvals` | Org Operations | Approval and delegation workflows for staff actions. | `boolean` | false | false | false | true |
| `api.access` | Integrations | API key and integration endpoint access. | `boolean` | false | false | false | true |
| `webhooks.access` | Integrations | Outbound webhooks for operational events. | `boolean` | false | false | false | true |
| `security.advanced` | Security | Advanced security controls and policy options. | `boolean` | false | false | false | true |
| `support.priority` | Support | Priority support queue handling. | `boolean` | false | false | true | true |
| `support.account_manager` | Support | Dedicated account manager. | `boolean` | false | false | false | true |

---

## Enforcement Notes

- Quotas must be enforced in service layer, not only controller/policy attributes.
- For `count_window` features, use rolling windows (not calendar month reset) unless product policy changes.
- `players.search.access=preview` should return constrained results and masked contact details.
- `communication.sms.followers` is narrower than bulk SMS campaigns and requires confirmed recipient SMS consent, updated consent copy, matching A2P 10DLC campaign samples, STOP/HELP handling, and send/audit limits before enablement.
- `team_professional` and `enterprise_organization` should include all lower-tier features unless explicitly overridden.

## Plan Copy Guidance (UI)

Role labels should not imply access by themselves. UI should present:

- selected account role (`Coach`, `TeamManager`, `AcademyDirector`, `OrganizationAdmin`)
- active Team bundle tier
- unlocked features from active tier

Recommended wording pattern:

- "Role determines eligible plan options."
- "Active subscription determines unlocked features."

## Migration Notes From Current Docs

- Add new plan code `free_coach` to catalog and eligibility map.
- Keep existing codes `team_basic`, `team_professional`, `enterprise_organization`.
- Replace ambiguous "team bundle role description" text with entitlement-aware copy on account settings and onboarding.
