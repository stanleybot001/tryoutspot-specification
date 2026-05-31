# Flyer Import Automation - 2026-05-31

## Direction

Flyer import automation lives inside the existing TryOutSpot application, not a parallel site. The main app owns database writes, R2 flyer storage, validation, and listing creation. n8n can automate intake and AI extraction by calling the TryOutSpot admin API.

## MVP Flow

1. Admin or n8n adds a flyer import record from a Facebook post URL, external image URL, uploaded flyer file, or extracted JSON payload.
2. Flyer imports land in an admin-only review queue.
3. The reviewer confirms the structured fields: team, sport, opportunity type, event date, address, ZIP code, contact details, registration links, and notes.
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

Admin/n8n endpoints:

- `GET /api/admin/flyer-imports`
- `GET /api/admin/flyer-imports/{flyerImportId}`
- `POST /api/admin/flyer-imports`
- `POST /api/admin/flyer-imports/upload`
- `POST /api/admin/flyer-imports/{flyerImportId}/create-listing`

The API requires a platform admin token. n8n should authenticate through the normal API flow or a future service-account admin credential, then call these endpoints.

## n8n Workflow Shape

Recommended first workflow:

1. Manual trigger or webhook receives a Facebook post URL or image URL.
2. HTTP Request downloads the image while the Facebook CDN URL is still valid.
3. OpenAI vision extracts structured event data into JSON.
4. Function/Set node normalizes fields and maps opportunity type.
5. HTTP Request uploads the flyer and extracted fields to `POST /api/admin/flyer-imports/upload`.
6. Send a short notification to the admin review channel with the review URL.
7. On failure, send the source URL and error details to an error channel without writing directly to Postgres.

## Migration

Migration added: `20260531222639_AddFlyerImports`.

This migration was generated locally. It still needs to be applied to the target database during deployment.
