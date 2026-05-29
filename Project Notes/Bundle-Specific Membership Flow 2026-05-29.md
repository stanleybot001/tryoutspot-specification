# Bundle-Specific Membership Flow - 2026-05-29

## Decision

Parent/Player and Team membership are managed as separate subscription bundles on one user account.

This preserves the multiple-subscription model already introduced on 2026-05-12:

- A parent/player account can hold `premium_player`.
- A team representative account can separately hold `team_basic`, `team_professional`, or `enterprise_organization`.
- A mixed-role account can hold one active player-side subscription and one active team-side subscription at the same time.

## Web Flow Behavior

- `/account/onboarding/choose-plan?bundle=player_parent` shows only Free Player/Parent and Premium Player options.
- `/account/onboarding/choose-plan?bundle=team` shows Free Coach and paid team/organization options.
- Existing account-scoped subscription rows remain valid. The web flow classifies rows by `PlanType` instead of rewriting `ScopeType`.
- Settings displays Parent/Player and Team membership lanes separately.

## Downgrade And Cancellation Behavior

- Downgrading Parent/Player to Free Player/Parent schedules only active paid Parent/Player subscription cancellation.
- Downgrading Team to Free Coach schedules only active paid Team bundle subscription cancellation.
- Bundle-specific cancellation forms include `bundleType` so canceling Team billing does not cancel Premium Player, and canceling Parent/Player billing does not cancel Team billing.
- The legacy cancel endpoint without `bundleType` still cancels all active paid memberships for backward compatibility.

## Verification

Added regression coverage for:

- Settings rendering separate Parent/Player and Team membership summaries.
- Mixed-role user adding Team Basic while keeping Premium Player active.
- Team-only downgrade to Free Coach while Premium Player remains active.
- Team-only cancellation while Premium Player remains active.

Full test project passed with isolated output because the local running web app locked the normal `bin` output:

`dotnet test Application\TryOutSpot.Web.Tests\TryOutSpot.Web.Tests.csproj --no-build --verbosity minimal /p:OutputPath=C:\tmp\TryOutSpotBuild\bin\`
