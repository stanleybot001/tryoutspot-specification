# Entitlements and Billing Foundation 2026-05-01

## Decision

TryOutSpot will gate paid behavior through feature entitlements, not through account roles alone.

Account roles describe who the user is:

- Parent
- Player
- Coach
- TeamManager
- AcademyDirector
- OrganizationAdmin
- PlatformAdmin

Billing plans describe what the user has paid for or is trialing.

This keeps the account model flexible. A user can be both a parent and coach, and paid features can be added from the active plan without losing the free capabilities from each account type.

## Current Plan Catalog

| Plan code | Name | Audience | Monthly | Annual | Trial | Stripe required |
| --- | --- | --- | ---: | ---: | ---: | --- |
| `free_player_parent` | Free Player/Parent | Player/Parent | $0 | n/a | n/a | No |
| `premium_player` | Premium Player | Player/Parent | $9.99 | $99 | n/a | Yes |
| `team_basic` | Basic Team | Team/Academy | $29 | n/a | 30 days | Yes |
| `team_professional` | Professional Team | Team/Academy | $79 | $799 | n/a | Yes |
| `enterprise_organization` | Enterprise Organization | Organization | $199 | $1,999 | n/a | Yes |

## Free Services

Free player/parent features:

- Browse public opportunities
- Create basic player profiles
- Apply or register for opportunities
- Use basic opportunity-related team communication
- View registration and application status

Team posting and player discovery tools are not free by role alone. The current product model says team accounts receive a 30-day Basic Team trial, then convert to paid access.

## Paid Services

Premium Player features:

- Priority application review
- Advanced opportunity filters
- Enhanced player profile
- Direct team messaging where safety rules allow
- Player-side application analytics
- Early access to eligible opportunities

Basic Team features:

- Post up to five opportunities per month
- Basic player search
- Standard registration management
- Basic team analytics
- Email support

Professional Team features:

- Unlimited opportunity postings
- Advanced player search
- Premium registration management
- Detailed team analytics
- Priority support
- Custom branding
- Bulk communication

Enterprise Organization features:

- Professional Team features
- Multi-team management
- API access
- Custom workflows
- Dedicated account manager
- White-label options
- Advanced security

## Implementation Notes

- Feature codes live in `TryOutSpot.Web.Billing.TryOutSpotFeatureCodes`.
- Plan codes live in `TryOutSpot.Web.Billing.TryOutSpotPlanCodes`.
- The plan and feature catalog lives in `TryOutSpot.Web.Billing.TryOutSpotBillingCatalog`.
- `IEntitlementService` returns the union of free role features and active or trialing subscription features.
- Subscription statuses that currently grant paid features are `active` and `trialing`.
- The current database `Subscriptions` table is still one-to-one with `Users`. That is acceptable for the first Stripe pass, but multi-plan billing may require subscription item tracking later.

## Stripe Direction

The Stripe integration should use hosted Checkout for subscriptions, Customer Portal for self-service plan/payment changes, and webhooks to keep local subscription status and entitlements synchronized.

Minimum webhook events for the first implementation:

- `checkout.session.completed`
- `invoice.paid`
- `invoice.payment_failed`
- `customer.subscription.updated`
- `customer.subscription.deleted`

Do not rely on Checkout success redirects alone to grant access.

## Account API Testing To Complete

Before calling the account API production-ready, cover these scenarios:

- Register with one account type
- Register with multiple account types
- Reject unsupported account type
- Reject public registration as `PlatformAdmin`
- Reject duplicate email
- Reject weak password
- Forgot password returns generic response for existing and missing email
- Reset password accepts valid token
- Reset password rejects invalid or expired token
- Soft delete marks account inactive and locked
- Soft-deleted account cannot reset password or continue as active
