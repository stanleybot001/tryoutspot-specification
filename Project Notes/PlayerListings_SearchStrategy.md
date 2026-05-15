# Player Listings Search Strategy

This note captures what parent/player listing search can use now, plus the next fields to add so discovery stays fast and relevant.

## Listing types to support for parents/players

- `pickup_player`
- `looking_for_team`
- `used_equipment`
- `wanted_equipment`
- `private_lessons`
- `training_partner`

## Search fields already implemented

- `listingType`
- `sportId`
- `zipCode`
- `city`
- `state`
- `q` (title/description/location text)
- `minPrice`
- `maxPrice`
- publication/searchability flags (`isPublished`, `isSearchable`, `isActive`)

## Recommended next search fields

- `distanceMiles` from a source ZIP:
  - Needed for “players/teams within X miles of ZIP”.
  - Requires ZIP-to-lat/lng lookup and cached geocoding.
- `ageGroup`:
  - Useful for filtering pickup requests and team-fit searches.
- `graduationYear`:
  - Important for recruiting and class-based team needs.
- `positions`:
  - Primary/secondary position filters (pitcher, catcher, etc.).
- `availabilityWindow`:
  - Date-based fit for tournaments, tryouts, or short-term roster gaps.
- `abilityLevel` by sport:
  - Supports filters like `A/AA/AAA` (baseball) or `A/B/C` (softball) once sport-level catalogs are finalized.

## Indexing direction

When `distanceMiles` goes live, plan these additions:

- `lat/lng` storage for searchable listings.
- Composite discovery index including:
  - active/searchable flags
  - `listingType`
  - `sportId`
  - geo columns (or a PostGIS geography index if enabled)
- Keep page size capped and default sort by newest published listings.
