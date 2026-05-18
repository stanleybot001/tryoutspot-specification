# Dashboard Activity Feed 2026-05-18

TryOutSpot now has a personalized post-login dashboard activity feed.

## Product behavior

- Parent/player accounts see new activity after login on the existing onboarding/dashboard page.
- Free parent/player access is intentionally constrained to tryout opportunities.
- Premium Player access can add tournaments, pickup opportunities, for-sale equipment, roster openings, camps, and clinics to the dashboard feed.
- Team representative accounts see newly added searchable player profiles and player listings.
- Account settings include dashboard activity checkboxes. These preferences only narrow what a user wants to see; they never grant access.

## Technical behavior

- `UserDashboardPreferences` stores:
  - selected activity type codes as JSON
  - `LastViewedAt`, used as the "new since last dashboard visit" cursor
- The MVC dashboard calls `IDashboardActivityService.GetRecentActivityAsync(..., markAsViewed: true)` so the feed is marked viewed after the page is built.
- API clients can read without marking viewed via `GET /api/dashboard/recent-activity`.
- API clients can explicitly advance the cursor with `POST /api/dashboard/recent-activity/viewed`.
- Preferences are exposed through:
  - `GET /api/dashboard/preferences`
  - `POST /api/dashboard/preferences`
  - `POST /account/settings/dashboard-activity`

## Guardrails

- Dashboard activity is built server-side from `IEntitlementService`.
- Saved preference codes are intersected with current entitlements before any feed query runs.
- Opportunity queries still require published, active, non-expired listings from active/searchable teams.
- Player listing and player profile queries still honor active/searchable visibility and coach-only player visibility for team users.
