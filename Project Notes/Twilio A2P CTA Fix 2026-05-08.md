# Twilio A2P CTA Fix - 2026-05-08

## Issue

Twilio rejected the TryOutSpot A2P 10DLC campaign with error `30909`, meaning the reviewer could not verify the Message Flow or Call to Action.

## App-Side Fixes

- Added a public `/sms-consent` page that documents the opt-in path, checkbox text, message types, STOP/HELP behavior, message frequency, rates, optional consent, and mobile-data sharing limits.
- Added direct Privacy Policy and Terms and Conditions links next to the SMS consent checkbox on registration and social-registration pages.
- Added `/sms-consent` to the footer so reviewers can find the SMS opt-in disclosure without account access.

## Campaign Resubmission Guidance

Use a CTA that points reviewers to the public registration page and evidence page. Avoid listing profile setup as an opt-in path until that page is built and publicly documented.

Suggested CTA:

End users opt in to TryOutSpot transactional SMS during account registration at https://tryoutspot.com/account/register. The phone number field is optional. The SMS consent checkbox is unchecked by default and is not required to create an account. The checkbox text states: "Yes, I agree to receive transactional SMS messages from TryOutSpot, including verification codes, account security notices, password reset notifications, and tryout registration updates. Message and data rates may apply. Message frequency varies. Reply STOP to opt out and HELP for help." The registration page links to https://tryoutspot.com/privacy-policy and https://tryoutspot.com/terms-and-conditions. Public SMS consent details are also available at https://tryoutspot.com/sms-consent.
