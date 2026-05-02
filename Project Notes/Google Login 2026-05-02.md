# Google Login

Date: 2026-05-02

## Current Status

Google login is the first completed external login provider for TryOutSpot.

The backend uses ASP.NET Core external authentication and Identity external logins. The flow starts at:

- `GET /api/social-login/challenge/Google`

Google returns to the ASP.NET Core handler callback:

- `/signin-google`

After the Google handler validates the OAuth response, it redirects internally to:

- `GET /api/social-login/callback?provider=Google`

## Google Cloud Setup

Create a Google OAuth web application client and configure these authorized redirect URIs:

- Local HTTPS testing: `https://localhost:<port>/signin-google`
- Production: `https://<production-domain>/signin-google`

Store the credentials outside tracked source files:

- `Authentication:Google:ClientId`
- `Authentication:Google:ClientSecret`
- `Authentication:Google:CallbackPath` defaults to `/signin-google`

## Google Cloud Client

Project ID:

- `tryoutspotcom`

OAuth client ID:

- `575439301138-7hu60kliqvdavo5hvvjcer7i8hvqalnc.apps.googleusercontent.com`

Configured JavaScript origins:

- `https://tryoutspot.com`
- `https://www.tryoutspot.com`
- `https://localhost:7079`
- `http://localhost:5230`

Configured redirect URIs:

- `https://tryoutspot.com/signin-google`
- `https://www.tryoutspot.com/signin-google`
- `https://localhost:7079/signin-google`
- `http://localhost:5230/signin-google`

The client secret is stored only in ignored local/deployment configuration and must not be committed.

## API Behavior

- Linked Google accounts receive the standard TryOutSpot JWT and refresh token response.
- New Google identities receive `409 Conflict` with a short-lived `externalLoginToken`.
- The frontend can use the external login token to create a new account with one or more public account types.
- Existing TryOutSpot accounts are not automatically linked by email. The user must sign in and explicitly link Google.
- Google email addresses are only marked confirmed when Google returns `email_verified=true`.
- Google profile image URLs are captured for future frontend profile use.

## Follow-Up Providers

Facebook and Apple/iCloud remain planned follow-up providers after Google is verified in production.
