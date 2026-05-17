# Stripe Team Bundle Cutover Runbook - 2026-05-16

## Decision

- Keep `free_coach` as an internal non-Stripe plan.
- Team paid ladder is:
  - `team_basic`
  - `team_professional`
  - `enterprise_organization`
- Do not keep legacy TryOutSpot plan variants.

## Current Stripe Objects In Use

### Products

- Basic Team: `prod_UVESSQz8UunuTB`
- Professional Team: `prod_UVESd6FOs5hOSo`
- Enterprise Organization: `prod_UVESDsN5b3f7rd`
- Legacy Offseason Hold: `prod_UW9x1YqGXDZY4v`

### Prices (current configured mapping)

- `team_basic` monthly: `price_1TWE0TLqfLl80mfTKtTyngNt` ($29)
- `team_professional` annual: `price_1TWE0ULqfLl80mfT7lfpTuoi` ($799)
- `enterprise_organization` annual: `price_1TWE0ULqfLl80mfTQuSQBZhP` ($1,999)

### Legacy prices to stop using for new checkout

- `team_professional` monthly: `price_1TWE0TLqfLl80mfTOozKTxJZ`
- `enterprise_organization` monthly: `price_1TWE0ULqfLl80mfTKLRMtdjx`
- Offseason Hold monthly: `price_1TX7e3LqfLl80mfT53INSMni`

## App Cutover Checklist

1. Remove legacy plan eligibility and UI exposure for Offseason Hold.
2. Keep Stripe plan map only for paid plans still offered.
3. Ensure checkout only allows intervals supported by catalog:
   - `team_basic`: monthly
   - `team_professional`: annual
   - `enterprise_organization`: annual
4. Ensure `free_coach` never calls Stripe checkout.

## Stripe Ops Checklist

1. Verify active subscriptions tied to legacy prices:
   - list subscriptions by price and status `active`
2. If any legacy active subscriptions exist, migrate or cancel them.
3. Keep only active prices used by current catalog mapping.
4. Keep products in Stripe for reporting history, but do not map legacy prices in app config.

## Optional Cleanup (safe for single-customer environment)

- Cancel subscriptions still attached to legacy prices after confirming no longer needed.
- Leave old products/prices as archival records in Stripe dashboard.

## Validation Steps

1. Start checkout for `team_basic` monthly and confirm Stripe session creation succeeds.
2. Start checkout for `team_professional` annual and confirm success.
3. Start checkout for `enterprise_organization` annual and confirm success.
4. Attempt unsupported intervals:
   - `team_professional` monthly -> must reject
   - `enterprise_organization` monthly -> must reject
5. Attempt `free_coach` checkout -> must reject (internal plan, no Stripe required).

