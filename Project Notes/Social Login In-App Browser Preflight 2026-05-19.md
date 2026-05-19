# Social Login In-App Browser Preflight

Date: 2026-05-19

## Context

Mobile users can arrive at TryOutSpot from apps such as X/Twitter, Facebook, Instagram, TikTok, Messenger, and similar apps that open links in a built-in browser. Google OAuth blocks embedded or in-app browsers with `403: disallowed_useragent`, and Facebook Login can also fail in embedded WebViews.

## Implementation

The web social login buttons continue to target:

- `/account/external-login?provider=Google`
- `/account/external-login?provider=Facebook`

`AccountController.ExternalLogin` now performs a server-side preflight before starting the external OAuth challenge:

- Normal desktop and supported mobile browsers continue directly to the provider.
- Known mobile in-app browsers and generic mobile WebViews render `ExternalLoginBrowserWarning`.
- The warning page explains that Google/Facebook sign-in should be continued in Safari, Chrome, or the phone's default browser.
- Android WebView users also receive an optional Chrome intent link.
- Login/register links pass a `source` query value so the warning page can send users back to the right email/password fallback page.

No AJAX is used. The preflight is a normal server-rendered GET flow.

## Detection Notes

The detector names common app browsers when the user-agent is specific enough, including X/Twitter, Facebook, Messenger, Instagram, TikTok, LinkedIn, Pinterest, Snapchat, Discord, Reddit, LINE, and WeChat. Unknown mobile WebViews receive the generic "this app's built-in browser" message.

Detection is intentionally limited to mobile user-agents so desktop/laptop browsers continue silently.
