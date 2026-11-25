# Initial Setup

Created May 1, 2026.

## Baseline

- Solution: `TryOutSpot.sln`
- Application: `Application/TryOutSpot.Web`
- Framework: ASP.NET Core MVC on `net9.0`
- Data access: Entity Framework Core
- Database provider: PostgreSQL via `Npgsql.EntityFrameworkCore.PostgreSQL`

## Current Scope

This is intentionally a blank MVC application with EF Core wired in but no domain models yet. The next development step is to inspect and document the existing `tryoutspot_prod` database structure before creating entities, DTOs, migrations, or API contracts.

## Local Configuration

The checked-in `appsettings.json` contains a placeholder connection string only. Do not store real database credentials in source control. Use user secrets, environment variables, or another local secret source for real values.

For machine or server-specific file configuration:

- Copy `Application/TryOutSpot.Web/appsettings.instance.template.json` to `appsettings.instance.json`.
- Fill in only the values required by that deployment.
- `appsettings.instance.json` is ignored by Git and included in local build and publish output when it exists.
- The application loads the instance file in every environment. Local development settings can override it through `appsettings.Local.json`.
- Environment variables and command-line arguments have the highest precedence.
- The template is documentation only and is excluded from build and publish output.
