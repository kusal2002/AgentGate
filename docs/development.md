# Development

Follow the root README to start PostgreSQL, the API, and the dashboard. No Slack or LLM credentials are necessary.

## Build and lint

```powershell
dotnet build backend/AgentGate.sln
npm --prefix dashboard ci
npm --prefix dashboard run build
npm --prefix dashboard run lint
```

Commit the npm lockfile. Use `npm ci` for reproducible installs. The shadcn configuration is `dashboard/components.json`; component source lives in `dashboard/src/components/ui`.

To add another official shadcn component, run `npx shadcn@4.21.3 add <component>` from `dashboard`. The scaffold CLI is not a runtime dependency. This registry version may generate imports from `cn`; use the existing `@/lib/utils` helper instead.

## EF Core migrations (when entities are added)

The pinned local `dotnet-ef` tool is recorded in `dotnet-tools.json`. From the repository root:

```powershell
dotnet tool restore
dotnet ef migrations add InitialSchema --project backend/AgentGate.Infrastructure --startup-project backend/AgentGate.Api --output-dir Persistence/Migrations
```

For database update, export database credentials first (or supply a full connection string through your process environment):

```powershell
$env:POSTGRES_PASSWORD = 'your local database password'
dotnet ef database update --project backend/AgentGate.Infrastructure --startup-project backend/AgentGate.Api
```

Use the other `POSTGRES_*` values if you changed the defaults. EF CLI does not load `.env` automatically. The startup project supplies configuration and dependency injection; the migration assembly is Infrastructure. Phase 1 has no entities and therefore no migration to apply.

## Troubleshooting

- **Docker command missing:** use your installed PostgreSQL with the credentials and database name from `.env`, or install Docker Desktop and reopen your terminal.
- **Database unavailable:** check `docker compose ps`, the database health check, credentials, port mapping, and `docker compose logs postgres`.
- **API unavailable:** start the backend and check `http://localhost:5000/health` directly. Ensure port 5000 is free.
- **Dashboard port in use:** stop the existing process or change Vite's port intentionally. It uses `strictPort` to avoid silently selecting a different port.
- **Existing PostgreSQL volume:** its initialized password persists even after editing `.env`.
- **Visual Studio:** open the root solution or `backend/AgentGate.sln`; both have the same project references.

## Phase boundaries

Phase 2 will add organizations, authentication, JWT, and roles. Critical authorization, tenant, race-condition, and idempotency tests belong with those implementations. No speculative business-rule tests are introduced for the Phase 1 scaffold.
