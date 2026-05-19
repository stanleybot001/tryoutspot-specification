# Complimentary Billing Grants

Implemented local billing grants so TryOutSpot can comp paid-plan access without changing Stripe subscriptions.

## Decisions

- Stripe remains the source for paid subscription lifecycle events.
- Complimentary access is local and additive: entitlement checks merge role-free features, active Stripe subscriptions, and active complimentary grants.
- Revoked or expired grants no longer contribute features.
- The first-1000 launch offer uses local grants for two months instead of Stripe coupons, so users can receive the startup promo without forcing immediate payment setup.
- The launch offer is campaign-backed. Admins can update the active campaign limit/duration or reset to a fresh campaign code for a later promo wave.
- Admin user profiles include grant/revoke controls for account-scoped paid-plan access.

## Data

- `PromotionCampaigns`
  - Stores the active launch promotion campaign code, name, claim limit, free-month duration, active flag, and admin audit fields.
- `ComplimentaryPlanGrants`
  - Stores user, plan, scope, start/end window, source, optional promotion code, admin reason, and revoke audit fields.
- `PromotionRedemptions`
  - Stores one row per user and promotion code to enforce one launch claim per account.

## API

- `POST /api/billing/promotions/launch-founder-offer/claim`
  - Authenticated verified users claim the two-month launch offer.
  - Maximum launch redemptions: 1000.
- `GET /api/admin/billing/promotions/launch-founder-offer/status`
  - Platform admin status for total claims, remaining slots, active grant count, latest grant end, and recent claim details.
- `POST /api/admin/billing/promotions/launch-founder-offer/settings`
  - Updates the active campaign name, claim limit, and free-month duration for future claims.
- `POST /api/admin/billing/promotions/launch-founder-offer/reset`
  - Starts a new active campaign code and resets the claim counter without revoking existing grants.
- `GET /api/admin/billing/grants`
- `POST /api/admin/billing/grants`
- `POST /api/admin/billing/grants/{grantId}/revoke`

## Admin UI

Platform admins can open `/admin/promotions` to monitor the launch offer, update claim limit/duration, or reset the campaign counter for a later promo wave.

Platform admins can open `/admin/users/{userId}` and manage complimentary access from the user's profile. The form supports:

- Paid plan selection based on the user's account types
- Optional month duration
- Optional exact end date
- Blank duration/end date for a forever grant
- Reason note
- Revoke with an optional revoke note

## Notes

The implementation intentionally does not cancel, update, or create Stripe subscriptions when a complimentary grant is issued. If a user has both an active Stripe subscription and an active complimentary grant, both are visible in billing state and active features are combined.
