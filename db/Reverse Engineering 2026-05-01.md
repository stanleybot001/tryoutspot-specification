# Reverse Engineering 2026-05-01

## Database Inspected

- Server name: `ssvcpro200`
- Host: `192.168.48.15`
- Port: `5432`
- Database: `tryoutspot_prod`
- Schema: `public`
- PostgreSQL version from memory: `15.15`

No database password is stored in this repository.

## Observed Schema

- `public` contains 19 tables.
- 18 application tables were reverse engineered into EF Core.
- `__EFMigrationsHistory` was intentionally excluded from entity scaffolding.
- The database currently records these EF migrations:
  - `20260316231655_InitialCreate`
  - `20260316235106_AddOpportunitiesSystem`
  - `20260317001614_AddMediaSystem`
- The local EF Core baseline migration was aligned to the latest applied migration ID: `20260317001614_AddMediaSystem`.

## Generated Application Files

- DbContext: `Application/TryOutSpot.Web/Data/AppDbContext.cs`
- Entities: `Application/TryOutSpot.Web/Data/Entities/`
- Migrations: `Application/TryOutSpot.Web/Data/Migrations/`

## Migration Strategy

The local project contains a baseline migration that creates the current schema from scratch for new databases. Because the existing production database already has migration `20260317001614_AddMediaSystem` in `__EFMigrationsHistory`, EF Core treats the baseline as already applied on `tryoutspot_prod`.

Future schema changes should be made through EF Core migrations:

1. Update entities/model configuration.
2. Run `dotnet ef migrations add <DescriptiveMigrationName>`.
3. Review the generated migration.
4. Generate/review SQL before production application when appropriate.
5. Apply with `dotnet ef database update` only after review.

## Verification

`dotnet ef database update` was run against `tryoutspot_prod` after baseline alignment. EF Core reported:

`No migrations were applied. The database is already up to date.`
