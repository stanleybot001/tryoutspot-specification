# Email, SMS, and JWT Configuration

Date: 2026-05-01

## Provider Choices

- Email: Resend is the first transactional email provider for TryOutSpot account emails.
- SMS: Twilio Programmable Messaging is the first SMS provider for phone verification.
- Tracked configuration keeps safe placeholders only.
- Live local values are stored in `Application/TryOutSpot.Web/appsettings.Local.json`.
- `appsettings.Local.json` is ignored by Git and must not be pushed.

## Configuration Files

Tracked defaults:

- `Application/TryOutSpot.Web/appsettings.json`
- `Application/TryOutSpot.Web/appsettings.Local.example.json`

Local-only values:

- `Application/TryOutSpot.Web/appsettings.Local.json`

The app loads `appsettings.Local.json` only in the `Development` environment. This keeps local editing simple while preventing live credentials from being committed to GitHub.

## Active Development Providers

Current local development settings use:

- `Email:Provider = Resend`
- `Sms:Provider = Twilio`

If live delivery should be disabled locally, set either provider back to `Logging`.

## Security Notes

- Do not copy live API keys into tracked files.
- Do not paste API keys into build notes, project notes, tests, or API documentation.
- Rotate provider keys if a secret is ever committed or shared outside the team.
- Production should eventually use deployment environment variables or a managed secret store, even though local development uses an ignored appsettings file for convenience.
