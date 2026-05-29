# Activation Assistance - 2026-05-29

## Decision

Team representatives who have stalled before posting their first listing should get a targeted assistance prompt, not a generic popup.

The first implementation is server-rendered on the account dashboard and API-first for future clients:

- `GET /api/dashboard/activation-assistance`
- `POST /api/dashboard/activation-assistance/dismiss`
- `POST /account/onboarding/activation-assistance/dismiss`

## Eligibility

The prompt is limited to active, email-confirmed team representative accounts when:

- the account is older than the configured signup grace period;
- the user has not dismissed the prompt within the cooldown window;
- the user manages no team, or manages an incomplete active team profile;
- no managed team has ever had a team opportunity listing.

Incomplete team profile checks are intentionally activation-focused:

- team level or age group;
- baseball/softball sport selection;
- city/state or ZIP code;
- contact email or phone;
- team search visibility.

Logo, social links, description, and other polish fields do not qualify as blockers.

## Configuration

`ActivationAssistance` options:

- `SupportEmail` defaults to `support@tryoutspot.com`.
- `WhatsAppUrl` is optional and hidden when blank.
- `SignupGraceHours` defaults to `24`.
- `DismissalCooldownDays` defaults to `14`.

Dismissals are stored in `ActivationAssistanceEvents`.
