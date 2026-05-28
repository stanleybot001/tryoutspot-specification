# TryOutSpot Billing Plans and Feature Matrix

Updated: May 28, 2026

## 1) Account Type -> Eligible Plans

| Account type | Free Player/Parent | Premium Player | Free Coach | Team Basic | Team Professional | Enterprise Organization |
|---|---:|---:|---:|---:|---:|---:|
| Parent | Yes | Yes | No | No | No | No |
| Player | Yes | Yes | No | No | No | No |
| Team representative (coach, manager) | No | No | Yes | Yes | Yes | Yes |

Note: Team representative eligibility covers coach/manager team account flows.

## 2) Feature Bundles (By Upgrade Layer)

## Free Player/Parent baseline
Compared to: Base package

- `opportunities.browse`: Search and view public tryouts, tournaments, camps, and roster openings.
- `opportunities.browse.free_rules`: Free discovery supports age filtering, tryout-only opportunity type, and geography radius up to 120 miles.
- `players.profiles.basic`: Create core player profiles for linked athletes.
- `opportunities.apply`: Register or apply for available opportunities.
- `communication.team.basic`: Receive and send basic opportunity-related communication.
- `registrations.status.view`: View registration and application status.

## Premium player add-ons
Compared to: Free Player/Parent

- `registrations.priority_review`: Flag applications for higher visibility to teams.
- `opportunities.search.advanced`: Unlock enhanced geography radius beyond free limits, full opportunity-type filtering (not tryout-only), and advanced level-based discovery filters.
- `players.profiles.enhanced`: Richer profiles with more detail, media, and highlight content.
- `communication.team.direct`: Coming soon - direct player-to-team messaging where allowed.
- `registrations.analytics.player`: Coming soon - player-side application insights and tracking.
- `opportunities.early_access`: Eligible opportunities earlier than standard release.

## Free Coach starter bundle
Compared to: No team plan

- `opportunities.post.limited`: Publish up to 1 tryout listing every 6 months.
- `opportunities.share.links`: Copy public listing links and ready-to-share post text for active opportunity listings.
- `opportunities.flyers`: Link or upload a PDF event flyer, save it with the opportunity, and display it on the public listing.
- `players.search.basic`: Limited player search (radius capped at 120 miles; age filters available; skill-level filters not included).

## Team Basic add-ons
Compared to: Free Coach

- `opportunities.post.limited`: Publish up to 9 tryout listings every 12 months.
- `players.search.advanced`: Full player discovery filters (age, level, radius, and related advanced search tools).
- `registrations.manage.standard`: Standard registration/applicant management.
- `communication.sms.followers`: Coming soon - short SMS listing updates to opted-in players and parents who follow an opportunity.
- `analytics.team.basic`: Basic team activity reporting.
- `support.email`: Standard email support.

## Team Professional add-ons
Compared to: Team Basic

- `opportunities.post.limited`: Publish up to 24 tryout listings every 12 months.
- `players.search.advanced`: Full player discovery filters (age, level, radius, and related advanced search tools).
- `registrations.manage.premium`: Enhanced applicant review and registration tooling.
- `analytics.team.detailed`: Deeper team analytics and conversion visibility.
- `support.priority`: Priority support queue.
- `branding.custom`: Team branding customization.
- `communication.bulk`: Bulk communication workflows.

## Enterprise Organization add-ons
Compared to: Team Professional

- `opportunities.post.limited`: Publish up to 50 tryout listings every 12 months.
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
- Discovery rules: Age filtering enabled; opportunity type restricted to tryouts; geography radius capped at 120 miles; level is view-only (no level filter/sort).

## Premium Player

- Audience: Player/Parent
- Price: $9.99/month or $99/year
- Adds over Free Player/Parent: Premium player add-ons
- Discovery upgrades over Free: Full opportunity-type filters, advanced level-based discovery filters, and expanded geography filtering beyond the free 120-mile radius cap.

## Free Coach

- Audience: Team/Academy (starter)
- Price: Free
- Includes: 1 listing every 6 months, basic player search, listing link sharing, and event flyer attachments
- Stripe required: No (internal plan)

## Team Basic

- Audience: Team/Academy
- Price: $29/month
- Includes: Team Basic add-ons, including follower SMS updates once SMS consent and carrier campaign language are approved

## Team Professional

- Audience: Team/Academy
- Price: $799/year (annual commitment only)
- Adds over Team Basic: Team Professional add-ons

## Enterprise Organization

- Audience: Organization
- Price: $1,999/year (annual commitment only)
- Adds over Team Professional: Enterprise Organization add-ons

## 4) Stripe and Enforcement Notes

As of May 16, 2026:

- `free_coach` is internal and does not create a Stripe checkout session.
- Stripe checkout is used for paid plan selection where Stripe keys and price IDs are configured.
- Webhook processing records processed Stripe event IDs to prevent duplicate handling.
- Checkout requests use idempotency keys to reduce duplicate Stripe checkout session creation.
- Team Professional and Enterprise Organization enforce annual billing at checkout.
- Entitlement-based authorization gates features by active/trialing subscription state and role-based free defaults.
