# ZIP + Radius Search Foundation

This document defines the shared location-search foundation that all discovery endpoints should use.

## Goals

- Use one consistent ZIP normalization and radius calculation path.
- Keep filters deterministic across players, teams, organizations, and opportunities.
- Keep query performance predictable with a two-step approach:
  1. ZIP catalog radius resolution
  2. Entity search filtered by resolved ZIP set

## Shared components

- `ZipCodeGeographies` table:
  - `ZipCode` (PK, normalized to 5-digit ZIP)
  - `City`, `State`
  - `Latitude`, `Longitude`
  - `IsActive`, `CreatedAt`
- `IZipRadiusSearchService` / `ZipRadiusSearchService`:
  - ZIP normalization
  - radius clamping
  - ZIP-in-radius resolution with bounding box + haversine

## Radius defaults

- Minimum: `1` mile
- Default: `25` miles
- Maximum: `250` miles

## API usage pattern (required for all search endpoints)

1. Normalize explicit `zipCode` filters with `NormalizeZipCode`.
2. If `originZipCode` is provided:
   - normalize ZIP
   - clamp radius
   - resolve catalog ZIPs using `ResolveZipCodesWithinRadiusAsync`
3. Apply ZIP set filter (`ZipCode IN (...)`) to the entity search query.
4. Optionally return `distanceMiles` in results when radius search is used.

## Parent/player listing types in scope

- `pickup_player`
- `looking_for_team`
- `used_equipment`
- `wanted_equipment`
- `private_lessons`
- `training_partner`

## Next rollout targets for shared radius filter

1. Player search endpoints
2. Team/organization discovery endpoints
3. Opportunity search endpoints
4. Any dashboard “near me” widgets

## Data loading requirement

Radius search depends on a populated ZIP catalog. Production should load national ZIP coordinates into `ZipCodeGeographies` before enabling wide-area search experiences.
