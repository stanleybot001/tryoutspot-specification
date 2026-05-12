# Stripe Membership System - 2026-05-11

## Completed

- Added the official `Stripe.net` package.
- Added configurable Stripe billing options under `Stripe` in appsettings.
- Kept Stripe secrets and Price IDs configurable; no real Stripe secret values are committed.
- Added flexible plan-to-price mapping by plan code and billing interval:
  - `premium_player`: monthly and annual
  - `team_basic`: monthly
  - `team_professional`: monthly and annual
  - `enterprise_organization`: monthly and annual
- Added `StripePriceId` to `Subscription` plus indexes for Stripe customer and subscription lookup.
- Added `POST /api/billing/checkout-session`.
- Added `POST /api/billing/customer-portal-session`.
- Added `POST /api/billing/stripe/webhook`.
- Added `GET /api/billing/me`.
- Updated the billing catalog response with configured monthly/annual price options.
- Centralized additive plan eligibility so checkout, onboarding, and account pages all use the same role-to-plan rules.

## Subscription Flow

1. User chooses a paid plan and billing interval.
2. App creates a Stripe Checkout session using the configured Stripe Price ID.
3. App records `checkout_started` locally without granting paid features.
4. Stripe sends `checkout.session.completed`.
5. App retrieves the Stripe subscription and syncs it into `Subscription`.
6. `IEntitlementService` grants paid features only when local subscription status is `active` or `trialing`.
7. Stripe subscription update/delete webhooks keep local status current for cancellation, failed payments, trialing, and active renewals.

## Stripe Dashboard Setup Needed

- Recurring Stripe Products and Prices were created in live mode on 2026-05-12.
- Put the Stripe secret key and webhook signing secret into `appsettings.Local.json` or server environment configuration.
- Configure the customer portal in Stripe for plan changes, payment method updates, and cancellation.
- Add the webhook endpoint:
  - `https://tryoutspot.com/api/billing/stripe/webhook`
- Subscribe to these events:
  - `checkout.session.completed`
  - `customer.subscription.created`
  - `customer.subscription.updated`
  - `customer.subscription.deleted`
- Put the webhook signing secret into `Stripe:WebhookSigningSecret`.

## Pricing Defaults

- Free Player/Parent: `$0`
- Premium Player: `$9.99/month` or `$99/year`
- Basic Team: `$29/month`, 30-day trial
- Professional Team: `$79/month` or `$799/year`
- Enterprise Organization: `$199/month` or `$1999/year`

## Live Stripe Catalog

| Plan | Product ID | Monthly Price ID | Annual Price ID |
| --- | --- | --- | --- |
| Premium Player | `prod_UVESGVXw45yULP` | `price_1TWE0SLqfLl80mfTjXWqloRG` | `price_1TWE0SLqfLl80mfTrQr2UB2R` |
| Basic Team | `prod_UVESSQz8UunuTB` | `price_1TWE0TLqfLl80mfTKtTyngNt` |  |
| Professional Team | `prod_UVESd6FOs5hOSo` | `price_1TWE0TLqfLl80mfTOozKTxJZ` | `price_1TWE0ULqfLl80mfT7lfpTuoi` |
| Enterprise Organization | `prod_UVESDsN5b3f7rd` | `price_1TWE0ULqfLl80mfTKLRMtdjx` | `price_1TWE0ULqfLl80mfTQuSQBZhP` |

## Notes

- Future subscription levels can be added by extending `TryOutSpotBillingCatalog` and adding matching Stripe Price IDs under `Stripe:Plans`.
- Plan eligibility is additive. Mixed-role users can select any plan eligible for one of their public account types, while single-role users are blocked from unrelated plans at checkout.
- State-changing billing endpoints use `POST`; no `PUT`, `PATCH`, or `DELETE` endpoints were introduced.
- Webhooks are the authority for granting/removing paid access. The success redirect alone does not unlock features.

## Tested

- Billing catalog includes configured price options.
- Checkout session creation records a pending local subscription without granting paid entitlements.
- Checkout rejects single-role users who request unrelated plans.
- Checkout allows mixed-role users such as `Parent + Coach`, `Player + Coach`, and `AcademyDirector` to request the union of eligible plans.
- Stripe-style subscription sync grants paid features on `active` and removes them on `canceled`.
- Full test suite passed: `64` tests.
