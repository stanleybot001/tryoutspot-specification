# Usability Pass (November 2026)

Site-wide pass to simplify player/parent, team, and admin flows while keeping the paid-plan limits that drive revenue.

## Monetization limits kept as-is

- Free Coach: 1 published listing per 6 months. Basic Team: 9 per 12 months. Professional Team: 24. Enterprise: 50.
- Online tryout registration requires Basic Team or higher.
- Free player search is capped at the free radius; the level filter stays premium.
- Upgrade prompts now link straight to `/account/onboarding/choose-plan?bundle=team` or `?bundle=player_parent` instead of generic pages.

## Player and parent flow

- Public tryout search at `/tryouts` for signed-out visitors (free-tier limits, sign-up banner). Signed-in users are redirected to the account search.
- The tryout a visitor started from is carried through register, email verification (via the `TryOutSpot.ReturnUrl` cookie, 3 days), sign-in, and add-child.
- Registration form: plain-language account types, optional fields collapsed.
- Child profile form: identity, sports, contact, school first; build, measurables, media, social, and recruiting links collapsed under "More profile details". The "manage this profile" checkbox was removed (always true) so parents cannot lock themselves out.
- Tryout registration prefills guardian name/email/phone from a parent account and the emergency contact from the user's most recent registration. Medical info is intentionally not prefilled.
- New My Registrations page (`/account/registrations`) with upcoming/past tabs and withdraw. Withdrawn registrations do not block re-registering.
- Saving a searchable player listing makes the player profile visible automatically.

## Team flow

- After creating a team, the coach lands on that team's listings page with a "Post a tryout" prompt.
- Listing quota is shown in plain language, with an upgrade link at the limit.
- Posting-limit and registration-plan errors now display next to the Publish/Registration fields.
- Tryout-day check-in page is interactive: name/number/school search, tap to check in or mark waiver received, live "X of Y checked in" counter. Updates use `fetch` with `X-Requested-With: fetch`; the attendance and waiver endpoints return JSON for those requests and otherwise redirect (back to check-in when `returnTo=check-in`). Print layout is unchanged.

## Admin

- Overview numbers link to their filtered queues.
- Deactivating or deleting a listing marks its open reports as Action taken. Report detail has a one-click Dismiss.

## Local marketing pages

- Public local pages: `/tryouts/{state}/{city-slug}` (for example `/tryouts/ks/wichita`) and `/tryouts/near/{zip}`. Both reuse the normal tryout search with the city's first catalog ZIP (lowest ZIP code) as the origin.
- Default radius is 65 miles (`TryOutSpotLocalPages.DefaultRadiusMiles`). Visitors can switch with `?radius=` chips (25/50/65/100/120); the search still caps at the viewer's plan limit (120 free).
- City slugs come from `TryOutSpotLocalPages.Slugify` against `ZipCodeGeographies.City`. Lookup does a direct lower-case name match first and only scans the state's ZIP list for punctuated names. `st-`/`ft-`/`mt-` abbreviations and uppercase states 301-redirect to the canonical slug (the catalog uses "Saint Louis", not "St. Louis"). Unknown cities return 404.
- Each page sets a title, meta description, and canonical URL; pages with zero results are `noindex, follow` so thin pages are not indexed.
- Admin "Local roundup" (`/admin/local-roundup`): ZIP + radius + sport + date window builds ready-to-post text (up to 10 upcoming listings, the local page link, and city/state hashtags such as `#WichitaBaseball #KSBaseball`). Copy button for Facebook/groups, and a shorter X version through `_SocialShareButtons`. Posting is manual; nothing auto-posts, and roundups contain team listings only (never player/minor profiles).
- Not yet built: a sitemap listing the city pages.

## Error pages

- `UseStatusCodePagesWithReExecute("/status/{0}")` renders `Views/Home/StatusCode.cshtml` for empty-body error responses (404, 403, 400, 413, 429, others) so browsers no longer show their own blank error screen. `/api` paths still get a bare status code. The oversized-upload and failed-registration messages moved into `HomeController.StatusCodePage`.
- `/opportunities/{id}`, `/player-listings/{id}`, and `/players/{id}` for an item the public can't see (expired, unpublished, deactivated, scheduled, or team hidden/suspended) still returns 404 to the public, but platform admins, the team's own staff (team listings), and the listing owner or player's managing parents (player pages) see the page with a "Not public" banner explaining why.
- Production WAF note: Plesk ModSecurity rule `931130` (OWASP CRS RFI off-domain link) blocks the Google sign-in callback because Google returns `iss` and `scope` values containing URLs. Disable that rule for tryoutspot.com.

## Shared front-end helpers

- `wwwroot/js/site.js`: copy-to-clipboard, `data-confirm`, player autofill, `data-exclusive-group` checkboxes.
- `wwwroot/js/check-in.js`: check-in page only.
- `Views/Shared/_StatusMessage.cshtml`: renders `TempData["StatusMessage"]` plus an optional local `StatusLinkUrl`/`StatusLinkLabel` action link.
