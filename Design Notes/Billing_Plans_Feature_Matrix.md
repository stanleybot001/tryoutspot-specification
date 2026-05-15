# TryOutSpot Billing Plans and Feature Matrix

Updated: May 14, 2026

## 1) Account Type -> Eligible Plans

| Account type | Free Player/Parent | Premium Player | Team Basic | Team Offseason Hold | Team Professional | Enterprise Organization |
|---|---:|---:|---:|---:|---:|---:|
| Parent | Yes | Yes | No | No | No | No |
| Player | Yes | Yes | No | No | No | No |
| Coach | No | No | Yes | Yes | Yes | No |
| TeamManager | No | No | Yes | Yes | Yes | No |
| AcademyDirector | No | No | Yes | Yes | Yes | Yes |
| OrganizationAdmin | No | No | No | No | Yes | Yes |

Note: OrganizationAdmin can now subscribe to **Team Professional** as well as Enterprise.
Note: **Team Offseason Hold** is only available to Team Basic-eligible roles (Coach, TeamManager, AcademyDirector).

## 2) Feature Bundles (By Upgrade Layer)

## Free Player/Parent baseline
Compared to: Base package

- `opportunities.browse`: Search and view public tryouts, tournaments, camps, and roster openings.
- `players.profiles.basic`: Create core player profiles for linked athletes.
- `opportunities.apply`: Register or apply for available opportunities.
- `communication.team.basic`: Receive and send basic opportunity-related communication.
- `registrations.status.view`: View registration and application status.

## Premium player add-ons
Compared to: Free Player/Parent

- `registrations.priority_review`: Flag applications for higher visibility to teams.
- `opportunities.search.advanced`: Enhanced filters for geography, level, age, and opportunity type.
- `players.profiles.enhanced`: Richer profiles with more detail, media, and highlight content.
- `communication.team.direct`: Direct player-to-team messaging where allowed.
- `registrations.analytics.player`: Player-side application insights and tracking.
- `opportunities.early_access`: Eligible opportunities earlier than standard release.

## Team Basic starter bundle
Compared to: No team plan

- `opportunities.post.limited`: Post up to 5 opportunities per month.
- `players.search.basic`: Basic player search and discovery.
- `registrations.manage.standard`: Standard registration/applicant management.
- `analytics.team.basic`: Basic team activity reporting.
- `support.email`: Standard email support.

## Team Offseason Hold
Compared to: Team Basic (retention option)

- `directory.team.searchable`: Keep team/organization directory listing active in search.
- `directory.team.contact.hidden`: Hide public contact details and social links while listed.

## Team Professional add-ons
Compared to: Team Basic

- `opportunities.post.unlimited`: Unlimited opportunity posting.
- `players.search.advanced`: Advanced player discovery filters.
- `registrations.manage.premium`: Enhanced applicant review and registration tooling.
- `analytics.team.detailed`: Deeper team analytics and conversion visibility.
- `support.priority`: Priority support queue.
- `branding.custom`: Team branding customization.
- `communication.bulk`: Bulk communication workflows.

## Enterprise Organization add-ons
Compared to: Team Professional

- `teams.manage.multiple`: Multi-team organization management.
- `api.access`: API-based integration access.
- `workflows.custom`: Organization-specific custom workflows.
- `support.account_manager`: Dedicated account manager support.
- `branding.white_label`: White-label options.
- `security.advanced`: Advanced security controls.

## 3) Plan-by-Plan Summary

## Free Player/Parent

- Audience: Player/Parent
- Price: Free
- Includes: Free Player/Parent baseline bundle

## Premium Player

- Audience: Player/Parent
- Price: $9.99/month or $99/year
- Adds over Free Player/Parent: Premium player add-ons

## Team Basic

- Audience: Team/Academy
- Price: $29/month
- Includes: Team Basic starter bundle

## Team Offseason Hold

- Audience: Team/Academy
- Price: $15.99/month
- Includes: Searchable listing only while contact/social details stay hidden

## Team Professional

- Audience: Team/Academy
- Price: $799/year (annual commitment only)
- Adds over Team Basic: Team Professional add-ons

## Enterprise Organization

- Audience: Organization
- Price: $1,999/year (annual commitment only)
- Adds over Team Professional: Enterprise Organization add-ons

## 4) Stripe and Enforcement Notes

As of May 14, 2026:

- Stripe checkout is active for paid plan selection where Stripe keys and price IDs are configured.
- Webhook processing now records processed Stripe event IDs to prevent duplicate handling.
- Checkout requests now use idempotency keys to reduce duplicate Stripe checkout session creation.
- Team Professional and Enterprise Organization enforce annual billing at checkout.
- If Team Professional or Enterprise Organization is canceled (including cancel-at-period-end intent), team and organization records are soft-deactivated and cannot be reactivated for a later season.
- Deactivated Team Professional/Enterprise records must be recreated as new records for a future season.
- Team Offseason Hold keeps directory visibility but applies contact-hidden behavior to team and organization public presence.
- Entitlement-based authorization now gates:
  - Player profile onboarding via `players.profiles.basic`
  - Team/organization onboarding via team posting entitlements (`opportunities.post.limited` or `opportunities.post.unlimited`)
