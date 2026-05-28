# Twilio SMS Cutover - 2026-05-27

## Approved A2P 10DLC Setup

- Brand registration SID: `BN36e1a2d2c64d4f367d0c8e538df8da63`
- Verified campaign SID: `CM290baa45b7144e0a9d82257ada33dbe0`
- Verified linked Messaging Service SID: `MGfbbcc11db84f296622035a37e57a6de6`
- Sender number assigned to verified service: `+19132700798`
- Verified campaign use case: Low Volume Mixed

## Important Twilio Service Distinction

The Messaging Service `MG65a752bc6f6044214e648bd819a510ad` belongs to the older `TryoutAndTournamentServerAPI` service and is tied to a failed campaign. Do not configure the TryOutSpot app to use that service for production SMS.

The TryOutSpot app should send production SMS through `MGfbbcc11db84f296622035a37e57a6de6`, because that is the service attached to the verified campaign and now contains the `+19132700798` sender.

## App Configuration

Use environment variables or ignored local configuration:

```text
Sms__Provider=Twilio
Twilio__MessagingServiceSid=MGfbbcc11db84f296622035a37e57a6de6
```

Keep `Twilio__AccountSid` and `Twilio__AuthToken` in secrets only.

## Message Text

Phone verification SMS should stay aligned with the approved sample:

```text
TryOutSpot: Your verification code is {code}. Reply STOP to opt out, HELP for help.
```

## Verification Checklist

- Confirm `Sms:Provider` is `Twilio` in the target environment.
- Confirm `Twilio:MessagingServiceSid` is `MGfbbcc11db84f296622035a37e57a6de6`.
- Register or update a test account with SMS consent accepted.
- Send a phone verification code from `/account/settings` or `POST /api/account/send-phone-verification`.
- Confirm delivery in Twilio messaging logs.
- When using Twilio Console's "Try it out > Send an SMS" page, choose sender type `Messaging Service` and select `MGfbbcc11db84f296622035a37e57a6de6`; sending with sender type `Phone number` may bypass the verified campaign path and fail with carrier provisioning errors such as `30024`.
