# TryOutSpot Codex Instructions

These instructions apply to all work in this repository.

## Project Direction

- TryOutSpot is a baseball and softball tryout platform connecting players, parents, teams, academies, organizations, and administrators.
- Build API-first. The backend REST API comes before the web frontend.
- The web frontend will be added after the API surface is mature enough to support it.
- Existing product requirements live in `Design Notes/README.md` and `Design Notes/TryoutSpot_Specification.md`; read them before making feature or architecture decisions.
- The existing production database referenced by the specification is PostgreSQL database `tryoutspot_prod` on `ssvcpro200`. Do not assume schema details beyond documented or inspected database structure.

## Repository Layout

- `Application/` contains the ASP.NET Core application and solution projects.
- `Build Notes/` contains setup notes, implementation logs, and build/run decisions.
- `db/` contains database notes, schema exports, migration notes, seed data, and SQL review artifacts.
- `Design Notes/` contains product specifications and design reference material.
- `Notes/` contains temporary working notes.
- `Project Notes/` contains longer-lived project planning and decision records.

## Technical Defaults

- Use ASP.NET Core MVC as the base application shell.
- Use Entity Framework Core for data access.
- Use PostgreSQL for the application database unless the user explicitly changes direction.
- Keep the backend suitable for REST API development from the beginning.
- Use async database operations.
- Prefer strongly typed options/configuration over stringly typed access in application code.
- Do not commit secrets, connection strings, API keys, or production credentials.
- Store local-only secrets with user secrets or environment variables.

## API And Data Rules

- Design API endpoints with stable request/response contracts.
- Use only `GET` and `POST` for application endpoints unless the user explicitly approves other HTTP verbs.
- Use `POST` for creates, updates/edits, deletes/removals, and state-changing actions.
- Do not use `PUT`, `PATCH`, or `DELETE` by default because the target web server/security layer may reject those verbs.
- Keep SQL and EF Core queries performance-conscious; target sub-200ms queries where practical.
- Add pagination for list/search endpoints that can grow.
- Avoid loading large object graphs by default. Use projection DTOs for API responses.
- Prefer EF Core parameterization and LINQ translation over raw SQL. If raw SQL is needed, parameterize it.
- When adapting to the existing database, inspect and document schema first, then map entities carefully.

## Frontend Rules

- Keep JavaScript in views minimal unless the user explicitly approves more.
- Do not use AJAX in views unless explicitly requested.
- Build accessible, mobile-first UI when frontend work begins.
- The frontend should communicate with the REST API rather than duplicating backend behavior in views.

## Development Workflow

- Before adding features, check the relevant design/specification notes.
- Keep changes scoped to the requested feature or setup step.
- Add or update build notes when setup decisions, commands, or architectural choices matter later.
- Verify with `dotnet build` after project or code changes when feasible.
- For database-related changes, document assumptions in `db/` or `Project Notes/`.

## Initial Implementation Plan

1. Create the base ASP.NET Core MVC application and solution.
2. Inspect the current PostgreSQL database structure.
3. Adapt EF Core mappings to the existing database.
4. Build REST API endpoints for user registration, login, password reset, and account management.
5. Continue API-first feature development for each product area.
6. Build the web frontend once the supporting API contracts are established.
