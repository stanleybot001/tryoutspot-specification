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
