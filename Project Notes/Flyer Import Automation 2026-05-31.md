# Flyer Import Automation - 2026-05-31

## Direction

Flyer import automation lives inside the existing TryOutSpot application, not a parallel site. The main app owns AI extraction, database writes, R2 flyer storage, validation, review, and listing creation.

n8n is not required for the core workflow. It can be added later as an optional bulk-intake helper for scheduled Facebook collection, admin notifications, or other repetitive routing, but the website remains the system of record.

## MVP Flow

1. Admin adds a flyer from the existing admin website by uploading an image or pasting a public external image URL.
2. The website calls AI vision extraction and creates a pending flyer import with structured fields.
3. The reviewer confirms the extracted fields: team, sport, opportunity type, event date, address, ZIP code, contact details, registration links, and notes.
4. The reviewer creates a TryOutSpot team opportunity from the import.
5. New listings default to unpublished drafts unless the reviewer explicitly chooses immediate publish.

## Supported Flyer Categories

Flyer imports are not limited to tryouts. The import workflow supports:

- Tryouts
- Roster openings / adding players
- Guest or pickup player needs
- Tournaments
- Camps
- Clinics
- Private workouts
- Other

Common flyer text such as "adding players" maps to `roster_opening`, and "guest players" maps to `pickup_player`.

## Storage

Do not depend on Facebook CDN image URLs as the long-term source of truth. They can expire or stop working. When possible, download or upload the flyer into the existing Cloudflare R2-backed document storage and store the R2 object key on the import. When a draft listing is created, the stored flyer is copied to the opportunity document path so the listing can render its own flyer.

## API Surface

Admin/API endpoints:

- `GET /api/admin/flyer-imports`
- `GET /api/admin/flyer-imports/{flyerImportId}`
- `POST /api/admin/flyer-imports`
- `POST /api/admin/flyer-imports/upload`
- `POST /api/admin/flyer-imports/{flyerImportId}/create-listing`

The API requires a platform admin token. If n8n is added later, it should authenticate through the normal API flow or a future service-account admin credential, then call these endpoints.

## Optional n8n Workflow Shape

Potential later workflow:

1. Manual trigger or webhook receives a Facebook post URL or image URL.
2. HTTP Request sends the image URL or flyer file to the TryOutSpot admin API.
3. TryOutSpot performs AI extraction and creates the pending flyer import.
4. n8n sends a short notification to the admin review channel with the review URL.
5. On failure, send the source URL and error details to an error channel without writing directly to Postgres.

## Migration

Migration added: `20260531222639_AddFlyerImports`.

This migration was generated locally. It still needs to be applied to the target database during deployment.
